using Microsoft.ML.Data;

namespace Chatbot.Api.Ml;

public class IntentModelInput
{
    public string Text { get; set; } = string.Empty;

    // Eğitim pipeline'ı Label kolonunu içerdiği için ML.NET runtime şeması bu alanı bekler.
    // Tahmin sırasında gerçek bir label vermiyoruz; boş bırakıyoruz.
    public string Label { get; set; } = string.Empty;
}

public class IntentModelOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

public class IntentPredictionResult
{
    public string IntentName { get; set; } = string.Empty;
    public float ModelScore { get; set; }
    public string? SecondIntentName { get; set; }
    public float SecondBestScore { get; set; }
    public float ScoreMargin { get; set; }

    // Botun kalibre eşiklerine göre doğrudan cevap verilebilir mi (tek kelime kuralı dahil).
    public bool IsConfident { get; set; }

    // Doğrudan cevap verilemiyor ama cevap büyük olasılıkla ilk iki adaydan biri.
    public bool CanOfferTopTwo { get; set; }
}

// MlModels/{botKey}-intent-model.thresholds.json. Chatbot.Trainer kalibrasyonla üretir.
public class IntentThresholds
{
    public string BotKey { get; set; } = string.Empty;
    public DateTime? TrainedAtUtc { get; set; }
    public float MinimumScore { get; set; }
    public float MinimumMargin { get; set; }
    public float SingleWordMinimumScore { get; set; }
    public float SingleWordMinimumMargin { get; set; }
    public float PairMinimumMass { get; set; }
    public float PairMinimumSecondScore { get; set; }
}
