using Chatbot.Trainer.Ml;

namespace Chatbot.Trainer.Evaluation;

// Eşikleri, modelin eğitimde görmediği cümlelerdeki (çapraz doğrulama) davranışına bakarak seçer.
// Amaç: cevap verilen mesajların en az TargetPrecision kadarı doğru olsun, bu şartla mümkün
// olduğunca çok mesaja cevap verilsin.
public static class ThresholdCalibrator
{
    public const float SingleWordMinimumScore = 0.82f;
    public const float SingleWordMinimumMargin = 0.30f;
    public const float PairMinimumSecondScore = 0.10f;
    public const double PairTargetHitRate = 0.90;

    // Kalibrasyon hiçbir eşikte hedefi tutturamazsa kullanılır (eski sabit eşikler).
    public const float FallbackMinimumScore = 0.65f;
    public const float FallbackMinimumMargin = 0.15f;

    private const float PairDisabled = 1.01f;

    public static CalibrationResult Calibrate(IReadOnlyList<ScoredRow> rows, double targetPrecision)
    {
        var minimumAccepted = Math.Max(10, (int)Math.Ceiling(rows.Count * 0.05));
        var candidates = new List<GridPoint>();

        // 0.05 adımlı ızgara; tamsayı döngüsüyle kayan nokta birikmesinden kaçınıyoruz.
        for (var s = 5; s <= 18; s++)
        {
            for (var m = 0; m <= 8; m++)
            {
                var score = s * 0.05f;
                var margin = m * 0.05f;
                var accepted = rows.Where(r => Accepts(r, score, margin)).ToList();

                if (accepted.Count < minimumAccepted)
                    continue;

                candidates.Add(new GridPoint(
                    score,
                    margin,
                    Precision: (double)accepted.Count(r => r.IsTopCorrect) / accepted.Count,
                    Coverage: (double)accepted.Count / rows.Count));
            }
        }

        var meetsTarget = candidates.Where(x => x.Precision >= targetPrecision).ToList();
        var targetMet = meetsTarget.Count > 0;

        var chosen = targetMet
            ? meetsTarget
                .OrderByDescending(x => x.Coverage)
                .ThenByDescending(x => x.Precision)
                .ThenByDescending(x => x.Score)
                .First()
            : candidates
                .OrderByDescending(x => x.Precision)
                .ThenByDescending(x => x.Coverage)
                .FirstOrDefault()
              ?? new GridPoint(FallbackMinimumScore, FallbackMinimumMargin, 0, 0);

        var (pairMass, pairOffers, pairHitRate) = CalibratePair(rows, chosen.Score, chosen.Margin);

        var thresholds = new IntentThresholds
        {
            MinimumScore = chosen.Score,
            MinimumMargin = chosen.Margin,
            SingleWordMinimumScore = Math.Max(SingleWordMinimumScore, chosen.Score),
            SingleWordMinimumMargin = Math.Max(SingleWordMinimumMargin, chosen.Margin),
            PairMinimumMass = pairMass,
            PairMinimumSecondScore = PairMinimumSecondScore,
            TargetPrecision = targetPrecision,
            Evidence = new CalibrationEvidence
            {
                Samples = rows.Count,
                Precision = Math.Round(chosen.Precision, 4),
                Coverage = Math.Round(chosen.Coverage, 4),
                PairOffers = pairOffers,
                PairHitRate = Math.Round(pairHitRate, 4)
            }
        };

        return new CalibrationResult(thresholds, targetMet);
    }

    public static bool Accepts(ScoredRow row, float minimumScore, float minimumMargin)
    {
        if (row.WordCount <= 1)
        {
            minimumScore = Math.Max(minimumScore, SingleWordMinimumScore);
            minimumMargin = Math.Max(minimumMargin, SingleWordMinimumMargin);
        }

        return row.TopScore >= minimumScore && row.Margin >= minimumMargin;
    }

    public static bool Accepts(ScoredRow row, IntentThresholds thresholds) =>
        Accepts(row, thresholds.MinimumScore, thresholds.MinimumMargin);

    public static bool OffersPair(ScoredRow row, IntentThresholds thresholds) =>
        !Accepts(row, thresholds)
        && row.SecondScore >= thresholds.PairMinimumSecondScore
        && row.TopScore + row.SecondScore >= thresholds.PairMinimumMass;

    // Doğrudan cevaplanamayan mesajlarda iki seçenek sunmak için en düşük "toplam skor" sınırını seçer:
    // sunulan iki seçenekten biri en az %90 oranında doğru olmalı.
    private static (float Mass, int Offers, double HitRate) CalibratePair(
        IReadOnlyList<ScoredRow> rows,
        float minimumScore,
        float minimumMargin)
    {
        var notAccepted = rows
            .Where(r => !Accepts(r, minimumScore, minimumMargin) && r.SecondScore >= PairMinimumSecondScore)
            .ToList();

        for (var step = 6; step <= 19; step++)
        {
            var mass = step * 0.05f;
            var offered = notAccepted.Where(r => r.TopScore + r.SecondScore >= mass).ToList();

            if (offered.Count < 5)
                break;

            var hitRate = (double)offered.Count(r => r.IsInTopTwo) / offered.Count;
            if (hitRate >= PairTargetHitRate)
                return (mass, offered.Count, hitRate);
        }

        return (PairDisabled, 0, 0);
    }

    private sealed record GridPoint(float Score, float Margin, double Precision, double Coverage);
}

public sealed record CalibrationResult(IntentThresholds Thresholds, bool TargetMet);
