using Chatbot.Api.Services.ExternalData;

namespace Chatbot.Tests.TestHelpers;

// Nexora ürün servisinin yerine geçen sahte veri sağlayıcı. Ağa çıkmaz.
public sealed class FakeProductProvider : IExternalDataProvider
{
    public const string ProviderName = "fake-products";

    public string Name => ProviderName;

    // Sağlayıcıya gelen istekler (testte hangi mesajla çağrıldığını kontrol etmek için).
    public List<ExternalDataRequest> Requests { get; } = new();

    public bool CanHandle(string intent) => intent == "ProductPrice";

    public Task<ExternalDataAnswer> GetAnswerAsync(ExternalDataRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        // Menüden seçilince ürün adı istenir; ürün adı yazılınca fiyat verilir.
        var answer = request.IsMenuSelection
            ? new ExternalDataAnswer { Message = "Hangi ürünün fiyatını öğrenmek istiyorsunuz?", AwaitingInput = true }
            : new ExternalDataAnswer { Message = $"{request.Message} ürününün fiyatı 1.000 TL." };

        return Task.FromResult(answer);
    }
}
