using Microsoft.ML.Data;

namespace Chatbot.Trainer.Ml;

public class IntentTrainingData
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class IntentEvaluationRow
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}

// Bir tahminin eşik kararları için gereken özeti: ilk iki aday ve skorları.
public sealed class ScoredRow
{
    public string Text { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string TopIntent { get; init; } = string.Empty;
    public string SecondIntent { get; init; } = string.Empty;
    public float TopScore { get; init; }
    public float SecondScore { get; init; }
    public int WordCount { get; init; }

    public float Margin => TopScore - SecondScore;
    public bool IsTopCorrect => TopIntent == Label;
    public bool IsInTopTwo => TopIntent == Label || SecondIntent == Label;

    // slotLabels: Score vektöründeki her konumun hangi intent'e ait olduğu (modelin SlotNames bilgisi).
    public static ScoredRow From(IntentEvaluationRow row, IReadOnlyList<string> slotLabels)
    {
        var ranked = row.Score
            .Select((score, index) => (Score: float.IsFinite(score) ? score : 0f, Index: index))
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToArray();

        return new ScoredRow
        {
            Text = row.Text,
            Label = row.Label,
            TopIntent = ranked.Length > 0 ? slotLabels[ranked[0].Index] : string.Empty,
            SecondIntent = ranked.Length > 1 ? slotLabels[ranked[1].Index] : string.Empty,
            TopScore = ranked.Length > 0 ? ranked[0].Score : 0f,
            SecondScore = ranked.Length > 1 ? ranked[1].Score : 0f,
            WordCount = row.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        };
    }
}
