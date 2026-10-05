using Chatbot.Trainer.Evaluation;
using Chatbot.Trainer.Ml;

namespace Chatbot.Tests;

// Trainer'ın eşik seçimi: cevap verilen mesajlarda hedef doğruluğu tutturan en geniş eşik seçilmeli.
public class ThresholdCalibratorTests
{
    [Fact]
    public void Tek_kelimelik_mesajda_daha_siki_esik_uygulanir()
    {
        var oneWord = Row("CheckInTime", "CheckInTime", topScore: 0.70f, secondScore: 0.10f, words: 1);
        var threeWords = Row("CheckInTime", "CheckInTime", topScore: 0.70f, secondScore: 0.10f, words: 3);

        Assert.False(ThresholdCalibrator.Accepts(oneWord, minimumScore: 0.50f, minimumMargin: 0.10f));
        Assert.True(ThresholdCalibrator.Accepts(threeWords, minimumScore: 0.50f, minimumMargin: 0.10f));
    }

    [Fact]
    public void Kalibrasyon_hedef_dogrulugu_tutturan_esigi_secer()
    {
        // 100 mesajda model yüksek skorla doğru, 20 mesajda düşük skorla yanlış tahmin ediyor.
        var rows = Enumerable.Range(0, 100).Select(_ => Row("A", "A", topScore: 0.90f, secondScore: 0.05f))
            .Concat(Enumerable.Range(0, 20).Select(_ => Row("A", "B", topScore: 0.40f, secondScore: 0.30f)))
            .ToList();

        var result = ThresholdCalibrator.Calibrate(rows, targetPrecision: 0.97);

        Assert.True(result.TargetMet);
        Assert.True(result.Thresholds.MinimumScore > 0.40f, "yanlış tahminleri dışarıda bırakmalı");
        Assert.True(result.Thresholds.MinimumScore <= 0.90f, "doğru tahminleri kabul etmeli");
        Assert.True(result.Thresholds.Evidence.Precision >= 0.97);
    }

    [Fact]
    public void Hic_bir_esikte_hedef_tutmazsa_bu_bildirilir()
    {
        // Her skor seviyesinde yarı yarıya doğru/yanlış: %97 doğruluk mümkün değil.
        var rows = Enumerable.Range(0, 60)
            .Select(i => Row("A", i % 2 == 0 ? "A" : "B", topScore: 0.90f, secondScore: 0.05f))
            .ToList();

        var result = ThresholdCalibrator.Calibrate(rows, targetPrecision: 0.97);

        Assert.False(result.TargetMet);
    }

    private static ScoredRow Row(string label, string topIntent, float topScore, float secondScore, int words = 3) => new()
    {
        Text = "ornek mesaj",
        Label = label,
        TopIntent = topIntent,
        SecondIntent = "Diger",
        TopScore = topScore,
        SecondScore = secondScore,
        WordCount = words
    };
}
