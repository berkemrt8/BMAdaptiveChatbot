namespace Chatbot.Trainer.Evaluation;

// MlModels/{botKey}-intent-model.thresholds.json dosyasının içeriği.
// Chatbot.Api'deki Ml/IntentThresholds sınıfıyla aynı alanlara sahip olmalıdır.
public sealed class IntentThresholds
{
    public string BotKey { get; set; } = string.Empty;
    public DateTime TrainedAtUtc { get; set; }

    // Bu eşiklerin üstündeki tahminler doğrudan cevaplanır.
    public float MinimumScore { get; set; }
    public float MinimumMargin { get; set; }

    // Tek kelimelik mesajlar az bağlam taşıdığı için ayrıca daha sıkı eşik kullanılır.
    public float SingleWordMinimumScore { get; set; }
    public float SingleWordMinimumMargin { get; set; }

    // Doğrudan cevap verilemeyen mesajda ilk iki aday seçenek olarak sunulur:
    // ikinci aday en az PairMinimumSecondScore, ikisinin toplamı en az PairMinimumMass olmalı.
    // PairMinimumMass > 1 ise bu özellik bu bot için kapalıdır.
    public float PairMinimumMass { get; set; }
    public float PairMinimumSecondScore { get; set; }

    public double TargetPrecision { get; set; }
    public CalibrationEvidence Evidence { get; set; } = new();
}

// Eşiklerin hangi ölçümle seçildiği (çapraz doğrulama sonuçları).
public sealed class CalibrationEvidence
{
    public int Samples { get; set; }
    public double Precision { get; set; }
    public double Coverage { get; set; }
    public int PairOffers { get; set; }
    public double PairHitRate { get; set; }
}
