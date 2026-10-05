using Chatbot.Api.Ml;
using Microsoft.Extensions.ObjectPool;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Chatbot.Api.Services;

public class IntentService : IIntentClassifier
{
    private static readonly JsonSerializerOptions ThresholdsJson = new(JsonSerializerDefaults.Web);

    private readonly MLContext _mlContext;
    private readonly ILogger<IntentService> _logger;
    private readonly string _modelDirectory;
    private readonly IntentThresholds _defaultThresholds;
    private readonly ObjectPoolProvider _poolProvider = new DefaultObjectPoolProvider();
    private readonly object _reloadLock = new();

    // Singleton servis eşzamanlı isteklerden çağrılır. Her model dosyası bir kez yüklenir ve
    // dosya değiştiğinde (trainer yeni model kaydettiğinde) API yeniden başlatılmadan tekrar yüklenir.
    private readonly ConcurrentDictionary<string, LoadedModel> _models = new(StringComparer.OrdinalIgnoreCase);

    public IntentService(
        MLContext mlContext,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<IntentService> logger)
    {
        _mlContext = mlContext;
        _logger = logger;
        _modelDirectory = Path.Combine(
            environment.ContentRootPath,
            configuration["Ml:ModelDirectory"] ?? "MlModels");

        // Model için kalibre eşik dosyası yoksa kullanılır.
        _defaultThresholds = new IntentThresholds
        {
            MinimumScore = ParseSetting(configuration["Ml:MinimumScore"], 0.65f),
            MinimumMargin = ParseSetting(configuration["Ml:MinimumMargin"], 0.15f),
            SingleWordMinimumScore = 0.82f,
            SingleWordMinimumMargin = 0.30f,
            PairMinimumMass = 1.01f,
            PairMinimumSecondScore = 0.10f
        };
    }

    public bool IsModelReady(string botKey) => File.Exists(GetModelPath(botKey));

    public IntentThresholds? GetThresholds(string botKey) =>
        IsModelReady(botKey) ? GetModel(botKey).Thresholds : null;

    public IntentPredictionResult Predict(string message, string botKey)
    {
        var model = GetModel(botKey);
        var engine = model.Engines.Get();
        IntentModelOutput prediction;

        try
        {
            prediction = engine.Predict(new IntentModelInput
            {
                Text = TextNormalizer.Normalize(message),
                Label = string.Empty
            });
        }
        finally
        {
            model.Engines.Return(engine);
        }

        var ranked = prediction.Score
            .Select((score, index) => (Score: float.IsFinite(score) ? score : 0f, Index: index))
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToArray();

        var topScore = ranked.Length > 0 ? ranked[0].Score : 0f;
        var secondScore = ranked.Length > 1 ? ranked[1].Score : 0f;
        var margin = topScore - secondScore;
        var secondIntent = ranked.Length > 1 && model.SlotLabels.Length > ranked[1].Index
            ? model.SlotLabels[ranked[1].Index]
            : null;

        var t = model.Thresholds;

        // Tek kelime çok az bağlam taşır; "saat" gibi genel kelimelerin yüksek ama yanlış
        // bir intent'e gitmesini önlemek için daha sıkı eşik kullanılır.
        var isSingleWord = TextNormalizer.CountWords(message) <= 1;
        var minimumScore = isSingleWord ? t.SingleWordMinimumScore : t.MinimumScore;
        var minimumMargin = isSingleWord ? t.SingleWordMinimumMargin : t.MinimumMargin;
        var isConfident = topScore >= minimumScore && margin >= minimumMargin;

        return new IntentPredictionResult
        {
            IntentName = prediction.PredictedLabel,
            ModelScore = topScore,
            SecondIntentName = secondIntent,
            SecondBestScore = secondScore,
            ScoreMargin = margin,
            IsConfident = isConfident,
            CanOfferTopTwo = !isConfident
                && secondIntent is not null
                && secondScore >= t.PairMinimumSecondScore
                && topScore + secondScore >= t.PairMinimumMass
        };
    }

    // Model dosyası kuralı Chatbot.Trainer ile aynıdır: MlModels/{botKey}-intent-model.zip
    private string GetModelPath(string botKey)
    {
        var safeKey = Path.GetFileName(botKey ?? string.Empty);
        return Path.Combine(_modelDirectory, $"{safeKey}-intent-model.zip");
    }

    private LoadedModel GetModel(string botKey)
    {
        var modelPath = GetModelPath(botKey);

        if (!File.Exists(modelPath))
        {
            throw new InvalidOperationException(
                $"Intent modeli bulunamadı: {Path.GetFileName(modelPath)}. Önce Chatbot.Trainer projesini '{botKey}' için çalıştırın.");
        }

        var thresholdsPath = Path.ChangeExtension(modelPath, ".thresholds.json");
        var modelStamp = File.GetLastWriteTimeUtc(modelPath);
        var thresholdsStamp = File.Exists(thresholdsPath) ? File.GetLastWriteTimeUtc(thresholdsPath) : DateTime.MinValue;

        if (_models.TryGetValue(modelPath, out var current) && current.IsCurrent(modelStamp, thresholdsStamp))
            return current;

        lock (_reloadLock)
        {
            if (_models.TryGetValue(modelPath, out current) && current.IsCurrent(modelStamp, thresholdsStamp))
                return current;

            try
            {
                var loaded = Load(modelPath, thresholdsPath, modelStamp, thresholdsStamp);
                _models[modelPath] = loaded;

                _logger.LogInformation(
                    "{Model} yüklendi. Eşik: skor {Score}, margin {Margin}, iki seçenek sınırı {Pair}.",
                    Path.GetFileName(modelPath), loaded.Thresholds.MinimumScore, loaded.Thresholds.MinimumMargin, loaded.Thresholds.PairMinimumMass);

                return loaded;
            }
            catch (Exception ex) when (current is not null)
            {
                // Dosya tam o sırada yazılıyor olabilir; bu istekte eski modelle devam edilir.
                _logger.LogWarning(ex, "{Model} yeniden yüklenemedi, önceki model kullanılıyor.", Path.GetFileName(modelPath));
                return current;
            }
        }
    }

    private LoadedModel Load(string modelPath, string thresholdsPath, DateTime modelStamp, DateTime thresholdsStamp)
    {
        var model = _mlContext.Model.Load(modelPath, out _);
        var engines = _poolProvider.Create(new PredictionEnginePolicy(_mlContext, model));

        // Score vektöründeki her konumun hangi intent'e ait olduğu; ikinci adayın adını bulmak için gerekir.
        var engine = engines.Get();
        string[] slotLabels;

        try
        {
            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            engine.OutputSchema["Score"].GetSlotNames(ref slotNames);
            slotLabels = slotNames.DenseValues().Select(x => x.ToString()).ToArray();
        }
        catch (InvalidOperationException)
        {
            slotLabels = Array.Empty<string>();
        }
        finally
        {
            engines.Return(engine);
        }

        return new LoadedModel(engines, slotLabels, LoadThresholds(thresholdsPath), modelStamp, thresholdsStamp);
    }

    private IntentThresholds LoadThresholds(string path)
    {
        if (!File.Exists(path))
            return _defaultThresholds;

        try
        {
            return JsonSerializer.Deserialize<IntentThresholds>(File.ReadAllText(path), ThresholdsJson) ?? _defaultThresholds;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "{File} okunamadı, varsayılan eşikler kullanılıyor.", Path.GetFileName(path));
            return _defaultThresholds;
        }
    }

    private static float ParseSetting(string? value, float defaultValue)
    {
        return float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : defaultValue;
    }

    private sealed record LoadedModel(
        ObjectPool<PredictionEngine<IntentModelInput, IntentModelOutput>> Engines,
        string[] SlotLabels,
        IntentThresholds Thresholds,
        DateTime ModelStamp,
        DateTime ThresholdsStamp)
    {
        public bool IsCurrent(DateTime modelStamp, DateTime thresholdsStamp) =>
            ModelStamp == modelStamp && ThresholdsStamp == thresholdsStamp;
    }

    private sealed class PredictionEnginePolicy
        : IPooledObjectPolicy<PredictionEngine<IntentModelInput, IntentModelOutput>>
    {
        private readonly MLContext _mlContext;
        private readonly ITransformer _model;

        public PredictionEnginePolicy(MLContext mlContext, ITransformer model)
        {
            _mlContext = mlContext;
            _model = model;
        }

        public PredictionEngine<IntentModelInput, IntentModelOutput> Create() =>
            _mlContext.Model.CreatePredictionEngine<IntentModelInput, IntentModelOutput>(_model);

        public bool Return(PredictionEngine<IntentModelInput, IntentModelOutput> obj) => true;
    }
}
