namespace Chatbot.Api.Dtos;

public class BotDto
{
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = string.Empty;
    public HelpMenuDto? HelpMenu { get; set; }
}

// GET /api/bots listesindeki bir bot. Test arayüzü bot seçim listesini bundan doldurur.
public class BotSummaryDto
{
    public string PublicKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Bu bot için eğitilmiş bir ML modeli yüklenebiliyor mu?
    public bool ModelReady { get; set; }

    // Canlı veri sağlayıcısının adı (örn. "nexora-catalog"); bot yalnızca bilgi tabanını kullanıyorsa null.
    public string? DataProvider { get; set; }
}

public class HelpMenuDto
{
    public string Prompt { get; set; } = string.Empty;
    public List<HelpCategoryDto> Categories { get; set; } = new();

    // "peki bitişi?" gibi kısa takip sorularını tanımak için bot'a özel ifadeler.
    // Genel "peki" / "ya" kuralı her bot için zaten geçerlidir.
    public List<string> FollowUpPhrases { get; set; } = new();
}

public class HelpCategoryDto
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<HelpOptionDto> Items { get; set; } = new();
}

public class HelpOptionDto
{
    public string Title { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
}
