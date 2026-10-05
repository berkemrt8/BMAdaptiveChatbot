using Chatbot.Api.Ml;

namespace Chatbot.Api.Services;

// Bir mesajın hangi intent'e ait olduğunu tahmin eden bileşen.
// Uygulamada ML.NET modelini kullanan IntentService; testlerde tahmini testin belirlediği sahte bir sınıf.
public interface IIntentClassifier
{
    IntentPredictionResult Predict(string message, string botKey);
}
