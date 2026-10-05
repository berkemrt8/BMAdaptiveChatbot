namespace Chatbot.Api.Services.ExternalData;

// Canlı (dinamik) veri gerektiren intent'leri cevaplayan harici kaynak.
// Chatbot çekirdeği yalnızca bu sözleşmeyi bilir; hangi bot'un hangi provider'ı kullandığı
// appsettings.json -> ExternalData:BotProviders ile belirlenir.
public interface IExternalDataProvider
{
    // appsettings içinde kullanılan provider adı (örn. "nexora-catalog").
    string Name { get; }

    bool CanHandle(string intent);

    // Yalnızca CanHandle(intent) true olan intent'ler için çağrılır.
    Task<ExternalDataAnswer> GetAnswerAsync(ExternalDataRequest request, CancellationToken cancellationToken = default);
}

public sealed class ExternalDataRequest
{
    public string Intent { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    // Menüden seçildiğinde Message kullanıcının yazdığı değil menü başlığıdır;
    // provider bu durumda arama yapmak yerine gerekli bilgiyi (ürün adı, bütçe vb.) istemelidir.
    public bool IsMenuSelection { get; init; }
}

public sealed class ExternalDataAnswer
{
    public string Message { get; init; } = string.Empty;

    // true ise cevap bir sorudur (örn. "Hangi ürün?") ve bir sonraki kullanıcı mesajı
    // aynı intent için eksik bilgi olarak değerlendirilir.
    public bool AwaitingInput { get; init; }

    public bool IsUnavailable { get; init; }
}
