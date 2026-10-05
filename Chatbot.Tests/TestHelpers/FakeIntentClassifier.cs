using Chatbot.Api.Ml;
using Chatbot.Api.Services;

namespace Chatbot.Tests.TestHelpers;

// Gerçek ML modeli yerine kullanılan sahte sınıflandırıcı: her test hangi mesaja
// hangi tahminin döneceğini kendisi belirler. Böylece testler modelden bağımsızdır.
public sealed class FakeIntentClassifier : IIntentClassifier
{
    private readonly Dictionary<string, IntentPredictionResult> _predictions = new();

    // Model kaç kez çağrıldı (örn. "alias eşleşince modele hiç sorulmadı" kontrolü için).
    public int Calls { get; private set; }

    // Özel bir tahmin tanımlanmamış mesajlarda model hiçbir intent'ten emin değildir.
    public IntentPredictionResult Default { get; set; } = Unsure("BreakfastTime", 0.20f, "CheckInTime", 0.15f);

    public void Returns(string message, IntentPredictionResult prediction) => _predictions[message] = prediction;

    public IntentPredictionResult Predict(string message, string botKey)
    {
        Calls++;
        return _predictions.TryGetValue(message, out var prediction) ? prediction : Default;
    }

    public static IntentPredictionResult Confident(string intent, float score = 0.95f) => new()
    {
        IntentName = intent,
        ModelScore = score,
        SecondBestScore = 0.02f,
        ScoreMargin = score - 0.02f,
        IsConfident = true
    };

    public static IntentPredictionResult Unsure(
        string intent,
        float score,
        string? secondIntent = null,
        float secondScore = 0f,
        bool canOfferTopTwo = false) => new()
    {
        IntentName = intent,
        ModelScore = score,
        SecondIntentName = secondIntent,
        SecondBestScore = secondScore,
        ScoreMargin = score - secondScore,
        IsConfident = false,
        CanOfferTopTwo = canOfferTopTwo
    };
}
