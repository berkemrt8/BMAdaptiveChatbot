namespace Chatbot.Api.Services.ExternalData;

public class ExternalDataProviderRegistry
{
    private readonly Dictionary<string, IExternalDataProvider> _providers;
    private readonly IConfiguration _configuration;

    public ExternalDataProviderRegistry(IEnumerable<IExternalDataProvider> providers, IConfiguration configuration)
    {
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _configuration = configuration;
    }

    // Bot için provider tanımlı değilse null döner; bu durumda tüm cevaplar KnowledgeArticles'tan gelir.
    public IExternalDataProvider? GetForBot(string botKey)
    {
        var providerName = _configuration[$"ExternalData:BotProviders:{botKey}"];

        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        return _providers.TryGetValue(providerName, out var provider)
            ? provider
            : throw new InvalidOperationException($"'{botKey}' için tanımlı '{providerName}' veri sağlayıcısı kayıtlı değil.");
    }
}
