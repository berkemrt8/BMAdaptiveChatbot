using Chatbot.Trainer.Evaluation;
using Chatbot.Trainer.Ml;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

// Kullanım:
//   dotnet run --project Chatbot.Trainer -- <botKey> [seçenekler]
//
//   --api <url>                Uçtan uca testin gönderileceği Chatbot.Api adresi (varsayılan http://localhost:5080)
//   --no-e2e                   Eğitimden sonra uçtan uca testi çalıştırma
//   --e2e-only                 Eğitim yapma, yalnızca çalışan API'ye uçtan uca testi uygula
//   --target-precision <0-1>   Cevap verilen mesajlarda hedeflenen doğruluk (varsayılan 0.97)
//
// Dataset: Data/{botKey}/training.tsv + Data/{botKey}/regression.tsv
// Çıktı  : Chatbot.Api/MlModels/{botKey}-intent-model.zip
//          Chatbot.Api/MlModels/{botKey}-intent-model.thresholds.json
//          Chatbot.Trainer/Data/{botKey}/e2e-report.json

const int FoldCount = 5;

Console.OutputEncoding = Encoding.UTF8;

var mlContext = new MLContext(seed: 42);
var dataRoot = Path.Combine(AppContext.BaseDirectory, "Data");
var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

if (!TryParseOptions(args, out var options))
{
    PrintUsage();
    return 1;
}

var botKey = options.BotKey;
var trainingPath = Path.Combine(dataRoot, botKey, "training.tsv");
var regressionPath = Path.Combine(dataRoot, botKey, "regression.tsv");
var modelPath = Path.Combine(solutionRoot, "Chatbot.Api", "MlModels", $"{botKey}-intent-model.zip");
var thresholdsPath = Path.ChangeExtension(modelPath, ".thresholds.json");
var reportPath = Path.Combine(solutionRoot, "Chatbot.Trainer", "Data", botKey, "e2e-report.json");

Console.WriteLine("Adaptive Chatbot - ML.NET Intent Trainer");
Console.WriteLine("--------------------------------------------");
Console.WriteLine($"Bot                : {botKey}");
Console.WriteLine($"Training dataset   : {trainingPath}");
Console.WriteLine($"Regression tests   : {regressionPath}");

if (options.EndToEndOnly)
{
    Console.WriteLine();
    Console.WriteLine("Uçtan uca test (eğitim yapılmadı)");
    return await RunEndToEndAsync(expectedTrainedAtUtc: null) ? 0 : 1;
}

Console.WriteLine($"Hedef doğruluk     : %{options.TargetPrecision * 100:0.#}");

var trainingRows = LoadRows(trainingPath);
var regressionRows = LoadRows(regressionPath);
ValidateDatasets(trainingRows, regressionRows);
PrintDatasetSummary(trainingRows, regressionRows);

// Label string olarak dataset içinde kalır.
// Trainer için LabelKey isimli sayısal kolon oluşturulur.
var pipeline = mlContext.Transforms.Conversion.MapValueToKey(
        outputColumnName: "LabelKey",
        inputColumnName: nameof(IntentTrainingData.Label))
    .Append(mlContext.Transforms.Text.FeaturizeText(
        outputColumnName: "Features",
        inputColumnName: nameof(IntentTrainingData.Text)))
    .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
        labelColumnName: "LabelKey",
        featureColumnName: "Features"))
    .Append(mlContext.Transforms.Conversion.MapKeyToValue(
        outputColumnName: "PredictedLabel",
        inputColumnName: "PredictedLabel"));

// 1) Her training cümlesi, onu görmemiş bir modelle tahmin edilir. Eşikler bu tahminlerden seçilir;
//    regression seti ise tamamen dışarıda kalır ve seçilen eşiklerin dürüst sınavı olur.
Console.WriteLine();
Console.WriteLine($"1) {FoldCount} katlı çapraz doğrulama ve eşik kalibrasyonu");
var outOfFold = CrossValidate(trainingRows);
Console.WriteLine($"Ham doğruluk        : {Percent(outOfFold.Count(x => x.IsTopCorrect), outOfFold.Count)}");

var calibration = ThresholdCalibrator.Calibrate(outOfFold, options.TargetPrecision);
var thresholds = calibration.Thresholds;
PrintThresholds(calibration);

Console.WriteLine();
Console.WriteLine("En çok karışan intent'ler (çapraz doğrulama):");
PrintConfusions(outOfFold);

// 2) Final model tüm training verisiyle eğitilir, regression setiyle sınanır.
Console.WriteLine();
Console.WriteLine($"2) Final model ({trainingRows.Count} örnek) ve unseen regression testi");
var finalModel = pipeline.Fit(mlContext.Data.LoadFromEnumerable(trainingRows));
var regressionScored = Score(finalModel, regressionRows);
PrintRegressionReport(regressionScored, thresholds);
PrintIntentConsistency(regressionScored, thresholds);

thresholds.BotKey = botKey;
thresholds.TrainedAtUtc = DateTime.UtcNow;
SaveModel(finalModel, thresholds);

Console.WriteLine();
Console.WriteLine($"Model kaydedildi   : {modelPath}");
Console.WriteLine($"Eşikler kaydedildi : {thresholdsPath}");

if (!options.RunEndToEnd)
    return 0;

Console.WriteLine();
Console.WriteLine($"3) Uçtan uca test ({options.ApiBaseUrl})");
await RunEndToEndAsync(thresholds.TrainedAtUtc);
return 0;

// ---------------------------------------------------------------------------

async Task<bool> RunEndToEndAsync(DateTime? expectedTrainedAtUtc)
{
    var rows = LoadRawRows(regressionPath);
    var report = await EndToEndEvaluator.RunAsync(options.ApiBaseUrl, botKey, rows, expectedTrainedAtUtc);

    if (report is null)
        return false;

    var previous = EndToEndEvaluator.LoadPrevious(reportPath);
    EndToEndEvaluator.Print(report, previous);
    EndToEndEvaluator.Save(report, reportPath);
    Console.WriteLine();
    Console.WriteLine($"Rapor kaydedildi   : {reportPath}");
    return true;
}

List<ScoredRow> CrossValidate(List<IntentTrainingData> rows)
{
    // Her intent'in örnekleri katlara eşit dağıtılır (stratified), sıralama seed ile sabittir.
    var random = new Random(42);
    var folds = rows
        .GroupBy(x => x.Label)
        .SelectMany(group => group
            .OrderBy(_ => random.Next())
            .Select((row, index) => (Row: row, Fold: index % FoldCount)))
        .ToList();

    var result = new List<ScoredRow>(rows.Count);

    for (var fold = 0; fold < FoldCount; fold++)
    {
        var train = folds.Where(x => x.Fold != fold).Select(x => x.Row).ToList();
        var test = folds.Where(x => x.Fold == fold).Select(x => x.Row).ToList();

        var model = pipeline.Fit(mlContext.Data.LoadFromEnumerable(train));
        result.AddRange(Score(model, test));
    }

    return result;
}

List<ScoredRow> Score(ITransformer model, List<IntentTrainingData> rows)
{
    var predictions = model.Transform(mlContext.Data.LoadFromEnumerable(rows));

    VBuffer<ReadOnlyMemory<char>> slotNames = default;
    predictions.Schema["Score"].GetSlotNames(ref slotNames);
    var slotLabels = slotNames.DenseValues().Select(x => x.ToString()).ToArray();

    return mlContext.Data
        .CreateEnumerable<IntentEvaluationRow>(predictions, reuseRowObject: false)
        .Select(row => ScoredRow.From(row, slotLabels))
        .ToList();
}

void SaveModel(ITransformer model, IntentThresholds modelThresholds)
{
    Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

    // API model dosyasını değişince yeniden yüklediği için önce geçici dosyaya yazıp sonra yer değiştiriyoruz;
    // böylece API hiçbir zaman yarım yazılmış bir dosya okumaz.
    var inputSchema = mlContext.Data.LoadFromEnumerable(new List<IntentTrainingData>()).Schema;
    var temporaryModelPath = modelPath + ".tmp";
    mlContext.Model.Save(model, inputSchema, temporaryModelPath);

    var json = JsonSerializer.Serialize(modelThresholds, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    File.WriteAllText(thresholdsPath + ".tmp", json);

    File.Move(thresholdsPath + ".tmp", thresholdsPath, overwrite: true);
    File.Move(temporaryModelPath, modelPath, overwrite: true);
}

void PrintThresholds(CalibrationResult result)
{
    var t = result.Thresholds;
    Console.WriteLine($"Seçilen eşik       : skor ≥ {t.MinimumScore:0.00}, margin ≥ {t.MinimumMargin:0.00}"
        + $" (eski sabit: {ThresholdCalibrator.FallbackMinimumScore:0.00} / {ThresholdCalibrator.FallbackMinimumMargin:0.00})");
    Console.WriteLine($"Tek kelime eşiği   : skor ≥ {t.SingleWordMinimumScore:0.00}, margin ≥ {t.SingleWordMinimumMargin:0.00}");
    Console.WriteLine($"Bu eşikte          : mesajların %{t.Evidence.Coverage * 100:0.0}'ine cevap, cevapların %{t.Evidence.Precision * 100:0.0}'i doğru");

    if (!result.TargetMet)
        Console.WriteLine($"UYARI: Hiçbir eşikte %{t.TargetPrecision * 100:0.#} doğruluğa ulaşılamadı; en yüksek doğruluk veren eşik seçildi. Veriyi güçlendirmek gerekiyor.");

    Console.WriteLine(t.PairMinimumMass <= 1
        ? $"İki seçenek önerisi: ilk iki adayın toplam skoru ≥ {t.PairMinimumMass:0.00} ise ({t.Evidence.PairOffers} örnekte, %{t.Evidence.PairHitRate * 100:0.0}'inde doğru cevap iki seçenekten biri)"
        : "İki seçenek önerisi: kapalı (güvenilir bir sınır bulunamadı)");
}

void PrintConfusions(List<ScoredRow> rows)
{
    var confusions = rows
        .Where(x => !x.IsTopCorrect)
        .GroupBy(x => (Expected: x.Label, Predicted: x.TopIntent))
        .Select(g => (g.Key.Expected, g.Key.Predicted, Count: g.Count()))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Expected)
        .Take(10)
        .ToList();

    if (confusions.Count == 0)
    {
        Console.WriteLine("- Karışan intent yok.");
        return;
    }

    foreach (var (expected, predicted, count) in confusions)
        Console.WriteLine($"- {expected,-24} → {predicted,-24} {count,3} kez");
}

void PrintRegressionReport(List<ScoredRow> rows, IntentThresholds t)
{
    var rawCorrect = rows.Count(x => x.IsTopCorrect);
    Console.WriteLine($"Test örneği         : {rows.Count}");
    Console.WriteLine($"Ham doğruluk        : {Percent(rawCorrect, rows.Count)}");

    PrintAcceptance("Eski sabit eşikle", rows.Where(x => ThresholdCalibrator.Accepts(x, ThresholdCalibrator.FallbackMinimumScore, ThresholdCalibrator.FallbackMinimumMargin)).ToList(), rows.Count);
    PrintAcceptance("Kalibre eşikle", rows.Where(x => ThresholdCalibrator.Accepts(x, t)).ToList(), rows.Count);

    var pairs = rows.Where(x => ThresholdCalibrator.OffersPair(x, t)).ToList();
    if (pairs.Count > 0)
        Console.WriteLine($"İki seçenek sunulan : {pairs.Count} mesaj, {Percent(pairs.Count(x => x.IsInTopTwo), pairs.Count)}'inde doğru cevap seçeneklerde");

    var wronglyAccepted = rows.Where(x => ThresholdCalibrator.Accepts(x, t) && !x.IsTopCorrect).ToList();
    if (wronglyAccepted.Count == 0)
        return;

    Console.WriteLine();
    Console.WriteLine("Yanlış cevaplanacak mesajlar (kalibre eşikle):");
    foreach (var row in wronglyAccepted)
        Console.WriteLine($"- {row.Text} | beklenen {row.Label} | tahmin {row.TopIntent} ({row.TopScore:0.00})");
}

void PrintAcceptance(string title, List<ScoredRow> accepted, int total)
{
    Console.WriteLine($"{title,-20}: {Percent(accepted.Count, total)} mesaja cevap, "
        + $"cevapların {Percent(accepted.Count(x => x.IsTopCorrect), accepted.Count)}'i doğru");
}

void PrintIntentConsistency(List<ScoredRow> rows, IntentThresholds t)
{
    Console.WriteLine();
    Console.WriteLine("Intent başına doğrudan doğru cevap (kalibre eşikle):");

    foreach (var group in rows.GroupBy(x => x.Label).OrderBy(x => x.Key))
    {
        var correct = group.Count(x => ThresholdCalibrator.Accepts(x, t) && x.IsTopCorrect);
        var total = group.Count();
        var rate = total == 0 ? 0 : (double)correct / total;
        var status = rate >= 0.90 ? "PASS" : "CHECK";
        Console.WriteLine($"- {group.Key,-24} {correct,2}/{total,2}  {rate,7:P1}  {status}");
    }
}

static string Percent(int part, int total) =>
    total == 0 ? "-" : $"%{(double)part / total * 100:0.0} ({part}/{total})";

List<IntentTrainingData> LoadRows(string path) =>
    LoadRawRows(path)
        .Select(x => new IntentTrainingData { Text = NormalizeText(x.Text), Label = x.Label })
        .Where(x => x.Text.Length > 0)
        .ToList();

// Uçtan uca testte cümleler API'ye kullanıcı yazmış gibi, normalize edilmeden gönderilir.
List<(string Text, string Label)> LoadRawRows(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException("Dataset bulunamadı.", path);

    var rows = new List<(string Text, string Label)>();

    foreach (var line in File.ReadLines(path).Skip(1))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        var parts = line.Split('\t');
        if (parts.Length < 2)
            continue;

        var text = parts[0].Trim();
        var label = parts[1].Trim();

        if (text.Length == 0 || label.Length == 0)
            continue;

        rows.Add((text, label));
    }

    return rows;
}

void ValidateDatasets(List<IntentTrainingData> training, List<IntentTrainingData> regression)
{
    var conflicts = training
        .GroupBy(x => x.Text)
        .Where(g => g.Select(x => x.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        .ToList();

    if (conflicts.Count > 0)
    {
        var samples = string.Join(", ", conflicts.Take(5).Select(x => x.Key));
        throw new InvalidOperationException($"Aynı training cümlesi birden fazla intent'e atanmış: {samples}");
    }

    var duplicateCount = training.Count - training
        .Select(x => $"{x.Label}\t{x.Text}")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    if (duplicateCount > 0)
        throw new InvalidOperationException($"Training dataset içinde {duplicateCount} yinelenen örnek var.");

    var trainingTexts = training
        .Select(x => x.Text)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var leakedRegression = regression
        .Where(x => trainingTexts.Contains(x.Text))
        .ToList();

    if (leakedRegression.Count > 0)
        throw new InvalidOperationException($"Regression dataset training ile çakışıyor. İlk örnek: {leakedRegression[0].Text}");

    var trainingLabels = training.Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var regressionLabels = regression.Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (!trainingLabels.SetEquals(regressionLabels))
        throw new InvalidOperationException("Training ve regression intent listeleri aynı değil.");
}

void PrintDatasetSummary(List<IntentTrainingData> training, List<IntentTrainingData> regression)
{
    Console.WriteLine();
    Console.WriteLine("Dataset özeti");
    Console.WriteLine($"Intent sayısı       : {training.Select(x => x.Label).Distinct().Count()}");
    Console.WriteLine($"Training örneği     : {training.Count}");
    Console.WriteLine($"Regression örneği   : {regression.Count}");
    Console.WriteLine();
    Console.WriteLine("Intent başına training / regression:");

    foreach (var group in training.GroupBy(x => x.Label).OrderBy(x => x.Key))
    {
        var regressionCount = regression.Count(x => x.Label == group.Key);
        Console.WriteLine($"- {group.Key,-24} {group.Count(),2} / {regressionCount,2}");
    }
}

// ML modeli bu normalizasyonla eğitilir; Chatbot.Api'deki TextNormalizer ile birebir aynı kalmalıdır.
string NormalizeText(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

    var lower = text.Trim().ToLower(new CultureInfo("tr-TR"))
        .Replace('ı', 'i')
        .Replace('ğ', 'g')
        .Replace('ü', 'u')
        .Replace('ş', 's')
        .Replace('ö', 'o')
        .Replace('ç', 'c');
    var builder = new StringBuilder(lower.Length);

    foreach (var character in lower)
    {
        builder.Append(char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)
            ? character
            : ' ');
    }

    return string.Join(' ', builder
        .ToString()
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

bool TryParseOptions(string[] arguments, out TrainerOptions parsed)
{
    parsed = new TrainerOptions();

    if (arguments.Length == 0 || arguments[0].StartsWith("--", StringComparison.Ordinal))
        return false;

    parsed.BotKey = arguments[0].Trim();

    for (var i = 1; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--no-e2e":
                parsed.RunEndToEnd = false;
                break;
            case "--e2e-only":
                parsed.EndToEndOnly = true;
                break;
            case "--api" when i + 1 < arguments.Length
                && Uri.TryCreate(arguments[i + 1].TrimEnd('/') + "/", UriKind.Absolute, out var api):
                parsed.ApiBaseUrl = api;
                i++;
                break;
            case "--target-precision" when i + 1 < arguments.Length
                && double.TryParse(arguments[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var precision)
                && precision is > 0 and <= 1:
                parsed.TargetPrecision = precision;
                i++;
                break;
            default:
                Console.WriteLine($"Tanınmayan veya eksik seçenek: {arguments[i]}");
                return false;
        }
    }

    return true;
}

void PrintUsage()
{
    var available = Directory.Exists(dataRoot)
        ? string.Join(", ", Directory.GetDirectories(dataRoot).Select(Path.GetFileName))
        : "-";

    Console.WriteLine("Kullanım: dotnet run --project Chatbot.Trainer -- <botKey> [--api <url>] [--no-e2e] [--e2e-only] [--target-precision 0.97]");
    Console.WriteLine($"Mevcut datasetler: {available}");
}

sealed class TrainerOptions
{
    public string BotKey { get; set; } = string.Empty;
    public Uri ApiBaseUrl { get; set; } = new("http://localhost:5080/");
    public bool RunEndToEnd { get; set; } = true;
    public bool EndToEndOnly { get; set; }
    public double TargetPrecision { get; set; } = 0.97;
}
