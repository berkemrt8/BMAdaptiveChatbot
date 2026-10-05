using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Chatbot.Api.Services.ExternalData;

// Nexora Integration API (/api/integration/v1) üzerinden canlı katalog verisi.
// Yalnızca yapılandırılmış REST sözleşmesini bilir; NexoraStoreDb'ye doğrudan bağlanmaz.
// Ayarlar: ExternalData:Providers:nexora-catalog:{BaseUrl, ApiKeyHeader, ApiKey, TimeoutSeconds}
public class NexoraCatalogProvider : IExternalDataProvider
{
    public const string ProviderName = "nexora-catalog";

    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    // Menüden seçildiğinde (ya da ürün bulunamadığında) kullanıcıdan istenecek bilgi.
    private static readonly Dictionary<string, string> InputPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProductPrice"] = "Fiyatını öğrenmek istediğiniz ürünün adını yazar mısınız? Örneğin: 'AstraBook Pro 14'.",
        ["ProductAvailability"] = "Stok durumunu öğrenmek istediğiniz ürünün adını yazar mısınız? Örneğin: 'NovaPhone X1 Pro 256'.",
        ["ProductSpecifications"] = "Teknik özelliklerini öğrenmek istediğiniz ürünün adını yazar mısınız? Örneğin: 'VisionQ 27 Pro'.",
        ["ProductSearch"] = "Hangi ürünü veya kategoriyi arıyorsunuz? Örneğin: 'kablosuz mouse' veya 'monitör'.",
        ["CheapestProduct"] = "Hangi kategoride en uygun fiyatlı ürünü arıyorsunuz? Örneğin: 'klavye' veya 'kulaklık'.",
        ["ProductBudgetSearch"] = "Bütçenizi ve aradığınız ürünü yazar mısınız? Örneğin: '5000 TL altında klavye'."
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _settings;

    public NexoraCatalogProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _settings = configuration.GetSection($"ExternalData:Providers:{ProviderName}");
    }

    public string Name => ProviderName;

    public bool CanHandle(string intent) => InputPrompts.ContainsKey(intent);

    public async Task<ExternalDataAnswer> GetAnswerAsync(ExternalDataRequest request, CancellationToken cancellationToken = default)
    {
        // Menü başlığı ("Bir ürünün fiyatını öğrenmek istiyorum") arama sorgusu olarak kullanılamaz.
        if (request.IsMenuSelection)
            return AskForInput(request.Intent);

        try
        {
            return request.Intent switch
            {
                "ProductAvailability" => await GetAvailabilityAsync(request, cancellationToken),
                "ProductPrice" => await GetPriceAsync(request, cancellationToken),
                "ProductSpecifications" => await GetSpecificationsAsync(request, cancellationToken),
                "ProductSearch" => await SearchProductsAsync(request, cancellationToken),
                "CheapestProduct" => await GetCheapestProductAsync(request, cancellationToken),
                "ProductBudgetSearch" => await SearchByBudgetAsync(request, cancellationToken),
                _ => throw new ArgumentException($"'{request.Intent}' intent'i bu sağlayıcıda tanımlı değil.", nameof(request))
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExternalDataAnswer
            {
                Message = "Nexora ürün servisi zaman aşımına uğradı. Lütfen kısa süre sonra tekrar deneyin.",
                IsUnavailable = true
            };
        }
        catch (HttpRequestException)
        {
            return new ExternalDataAnswer
            {
                Message = "Nexora ürün servisine şu anda bağlantı kurulamıyor.",
                IsUnavailable = true
            };
        }
    }

    private async Task<ExternalDataAnswer> GetAvailabilityAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var search = await GetProductsAsync(request.Message, inStock: null, sort: "relevance", limit: 5, cancellationToken: cancellationToken);
        var resolved = ResolveSingleProduct(request, search);

        if (resolved.Answer is not null)
            return resolved.Answer;

        var product = resolved.Product!;
        var answer = product.InStock && product.StockQuantity > 0
            ? $"{product.Name} şu anda stokta. Güncel stok: {product.StockQuantity} adet."
            : $"{product.Name} şu anda stokta görünmüyor.";

        return new ExternalDataAnswer { Message = answer };
    }

    private async Task<ExternalDataAnswer> GetPriceAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var search = await GetProductsAsync(request.Message, inStock: null, sort: "relevance", limit: 5, cancellationToken: cancellationToken);
        var resolved = ResolveSingleProduct(request, search);

        if (resolved.Answer is not null)
            return resolved.Answer;

        var product = resolved.Product!;
        return new ExternalDataAnswer
        {
            Message = $"{product.Name} ürününün güncel fiyatı {FormatPrice(product.Price)} TL."
        };
    }

    private async Task<ExternalDataAnswer> GetSpecificationsAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var search = await GetProductsAsync(request.Message, inStock: null, sort: "relevance", limit: 5, cancellationToken: cancellationToken);
        var resolved = ResolveSingleProduct(request, search);

        if (resolved.Answer is not null)
            return resolved.Answer;

        var product = resolved.Product!;

        if (product.Specifications.Count == 0)
        {
            return new ExternalDataAnswer
            {
                Message = $"{product.Name} için teknik özellik bilgisi bulunamadı."
            };
        }

        var details = string.Join("; ", product.Specifications.Take(8).Select(x => $"{x.Key}: {x.Value}"));
        return new ExternalDataAnswer
        {
            Message = $"{product.Name} — {details}."
        };
    }

    private async Task<ExternalDataAnswer> SearchProductsAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var category = DetectCategory(request.Message);
        var query = BuildCatalogSearchQuery(request.Message, category);
        var search = await GetProductsAsync(query, category: category, inStock: true, sort: "relevance", limit: 5, cancellationToken: cancellationToken);

        if (search.Items.Count == 0)
            return NoProductFound(request.Intent);

        return new ExternalDataAnswer
        {
            Message = $"Aramanıza uygun ürünler: {FormatProductList(search.Items)}"
        };
    }

    private async Task<ExternalDataAnswer> GetCheapestProductAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var category = DetectCategory(request.Message);
        var query = BuildCatalogSearchQuery(request.Message, category);
        var search = await GetProductsAsync(query, category: category, inStock: true, sort: "price-asc", limit: 1, cancellationToken: cancellationToken);

        if (search.Items.Count == 0)
            return NoProductFound(request.Intent);

        var product = search.Items[0];
        return new ExternalDataAnswer
        {
            Message = $"Bulduğum en uygun fiyatlı eşleşme {product.Name}. Güncel fiyatı {FormatPrice(product.Price)} TL."
        };
    }

    private async Task<ExternalDataAnswer> SearchByBudgetAsync(ExternalDataRequest request, CancellationToken cancellationToken)
    {
        var maxPrice = TryExtractBudget(request.Message);
        if (!maxPrice.HasValue)
        {
            return new ExternalDataAnswer
            {
                Message = "Bütçe sınırınızı TL olarak belirtir misiniz? Örneğin: '5000 TL altında klavye'.",
                AwaitingInput = true
            };
        }

        var category = DetectCategory(request.Message);
        var query = BuildCatalogSearchQuery(request.Message, category);
        var search = await GetProductsAsync(query, maxPrice: maxPrice, category: category, inStock: true, sort: "price-asc", limit: 5, cancellationToken: cancellationToken);

        if (search.Items.Count == 0)
            return NoProductFound(request.Intent);

        return new ExternalDataAnswer
        {
            Message = $"{maxPrice.Value.ToString("N0", TurkishCulture)} TL bütçenize uygun ürünler: {FormatProductList(search.Items)}"
        };
    }

    private async Task<ExternalProductSearchResponse> GetProductsAsync(
        string? query,
        decimal? maxPrice = null,
        string? category = null,
        bool? inStock = true,
        string sort = "relevance",
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _settings["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException($"ExternalData:Providers:{ProviderName}:BaseUrl yapılandırılmamış.");

        var apiKeyHeader = _settings["ApiKeyHeader"] ?? "X-Nexora-Integration-Key";
        var apiKey = _settings["ApiKey"] ?? string.Empty;
        var timeoutSeconds = int.TryParse(_settings["TimeoutSeconds"], out var parsedTimeout) ? parsedTimeout : 5;

        var url = new StringBuilder($"{baseUrl.TrimEnd('/')}/api/integration/v1/catalog/products?");
        if (!string.IsNullOrWhiteSpace(query))
            AddQuery(url, "query", query);
        if (!string.IsNullOrWhiteSpace(category))
            AddQuery(url, "category", category);
        if (maxPrice.HasValue)
            AddQuery(url, "maxPrice", maxPrice.Value.ToString(CultureInfo.InvariantCulture));
        if (inStock.HasValue)
            AddQuery(url, "inStock", inStock.Value ? "true" : "false");
        AddQuery(url, "sort", sort);
        AddQuery(url, "limit", Math.Clamp(limit, 1, 10).ToString(CultureInfo.InvariantCulture));

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation(apiKeyHeader, apiKey);

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException("External data API key rejected.");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ExternalProductSearchResponse>(cancellationToken)
            ?? new ExternalProductSearchResponse();
    }

    private static (ExternalProduct? Product, ExternalDataAnswer? Answer) ResolveSingleProduct(
        ExternalDataRequest request,
        ExternalProductSearchResponse search)
    {
        if (search.Items.Count == 0)
            return (null, NoProductFound(request.Intent));

        if (search.Items.Count == 1)
            return (search.Items[0], null);

        var normalizedMessage = $" {Normalize(request.Message)} ";
        var exactNameMatches = search.Items
            .Where(x => normalizedMessage.Contains($" {Normalize(x.Name)} ", StringComparison.Ordinal))
            .ToList();

        if (exactNameMatches.Count == 1)
            return (exactNameMatches[0], null);

        var choices = string.Join(" · ", search.Items.Take(4).Select(x => x.Name));
        return (null, new ExternalDataAnswer
        {
            Message = "Birden fazla ürün eşleşti. Hangi ürünü kastettiğinizi belirtir misiniz? " + choices,
            AwaitingInput = true
        });
    }

    private static ExternalDataAnswer AskForInput(string intent) => new()
    {
        Message = InputPrompts[intent],
        AwaitingInput = true
    };

    private static ExternalDataAnswer NoProductFound(string intent) => new()
    {
        Message = "Bu sorguya uygun aktif bir Nexora ürünü bulamadım. Ürün adını veya kategoriyi biraz daha açık yazar mısınız?",
        AwaitingInput = true
    };

    private static string FormatPrice(decimal price) => price.ToString("N2", TurkishCulture);

    private static string FormatProductList(IEnumerable<ExternalProduct> products) =>
        string.Join(" · ", products.Select(x => $"{x.Name} ({FormatPrice(x.Price)} TL)"));

    private static string? DetectCategory(string message)
    {
        var text = Normalize(message);
        var mappings = new (string Category, string[] Terms)[]
        {
            ("dizustu-bilgisayar", new[] { "laptop", "notebook", "dizustu" }),
            ("monitor", new[] { "monitor" }),
            ("klavye", new[] { "klavye" }),
            ("mouse", new[] { "mouse", "fare" }),
            ("kulaklik", new[] { "kulaklik", "headset" }),
            ("depolama", new[] { "ssd", "depolama", "disk" }),
            ("bilesen", new[] { "ekran karti", "gpu", "islemci", "cpu", "bilesen" }),
            ("ag-urunleri", new[] { "router", "modem" }),
            ("akilli-ev", new[] { "akilli ev", "smart home" }),
            ("telefon", new[] { "telefon", "smartphone" })
        };

        foreach (var mapping in mappings)
        {
            if (mapping.Terms.Any(term => text.Contains(Normalize(term), StringComparison.Ordinal)))
                return mapping.Category;
        }

        return null;
    }

    private static string? BuildCatalogSearchQuery(string message, string? category)
    {
        var text = Normalize(message);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hangi", "hangisi", "ne", "nedir", "var", "mi", "urun", "urunler", "urunleri",
            "goster", "listele", "ariyorum", "oner", "bana", "bul", "en", "ucuz", "uygun",
            "fiyatli", "hesapli", "dostu", "tl", "lira", "altinda", "alti", "kadar", "butce", "butceyle", "bin"
        };

        var categoryRoots = category switch
        {
            "dizustu-bilgisayar" => new[] { "laptop", "notebook", "dizustu", "bilgisayar" },
            "monitor" => new[] { "monitor" },
            "klavye" => new[] { "klavye" },
            "mouse" => new[] { "mouse", "fare" },
            "kulaklik" => new[] { "kulaklik", "headset" },
            "depolama" => new[] { "ssd", "depolama", "disk" },
            "bilesen" => new[] { "bilesen" },
            "ag-urunleri" => new[] { "router", "modem" },
            "akilli-ev" => new[] { "akilli", "ev" },
            "telefon" => new[] { "telefon", "smartphone" },
            _ => Array.Empty<string>()
        };

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !genericWords.Contains(token))
            .Where(token => !categoryRoots.Any(root => token.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            .Where(token => !Regex.IsMatch(token, @"^\d+(?:[\.,]\d+)?$"))
            .ToList();

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    // internal: Chatbot.Tests projesinden test edilebilsin diye (InternalsVisibleTo).
    internal static decimal? TryExtractBudget(string message)
    {
        // Normalize() noktalama işaretlerini sildiği için burada kullanılmıyor:
        // "12.500 TL" -> "12 500 tl" olur ve bütçe 500 okunurdu. Sayı ayraçları korunur.
        var text = message.Trim().ToLower(TurkishCulture).Replace('ı', 'i');

        var thousands = Regex.Match(text, @"(?<!\d)(?<n>\d+(?:[\.,]\d+)?)\s*bin\b");
        if (thousands.Success && TryParseDecimal(thousands.Groups["n"].Value, out var thousandValue))
            return thousandValue * 1000m;

        var currency = Regex.Match(text, @"(?<!\d)(?<n>\d[\d\.,]*)\s*(?:tl|lira)\b");
        if (currency.Success && TryParseDecimal(currency.Groups["n"].Value, out var currencyValue))
            return currencyValue;

        // Bütçe sorulduktan sonra kullanıcı yalnızca "5000" yazabilir.
        if (Regex.IsMatch(text, @"^\d[\d\.,]*$") && TryParseDecimal(text, out var plainValue))
            return plainValue;

        return null;
    }

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        value = 0;
        var compact = raw.Trim().Replace(" ", string.Empty);

        if (Regex.IsMatch(compact, @"^\d{1,3}(?:\.\d{3})+$"))
            compact = compact.Replace(".", string.Empty);
        else if (Regex.IsMatch(compact, @"^\d{1,3}(?:,\d{3})+$"))
            compact = compact.Replace(",", string.Empty);
        else
            compact = compact.Replace(',', '.');

        return decimal.TryParse(compact, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static void AddQuery(StringBuilder builder, string name, string value)
    {
        if (builder[^1] != '?')
            builder.Append('&');

        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(value));
    }

    private static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var value = input.Trim().ToLower(TurkishCulture)
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

        value = Regex.Replace(value, @"[^a-z0-9\s\-_]", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private sealed class ExternalProductSearchResponse
    {
        public int TotalCount { get; set; }
        public List<ExternalProduct> Items { get; set; } = new();
    }

    private sealed class ExternalProduct
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public bool InStock { get; set; }
        public Dictionary<string, string> Specifications { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
