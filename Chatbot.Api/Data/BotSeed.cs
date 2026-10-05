namespace Chatbot.Api.Data;

// Data/Seed/{botKey}.json dosyasının içeriği.
// Bu dosyalar yalnızca boş bir veritabanını ilk kez doldurmak için okunur; asıl kaynak veritabanıdır.
public sealed class BotSeed
{
    public string BotKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = string.Empty;
    public string FallbackMessage { get; set; } = string.Empty;
    public string MenuPrompt { get; set; } = string.Empty;
    public List<string> FollowUpPhrases { get; set; } = new();

    // intent adı -> cevap metni
    public Dictionary<string, string> Answers { get; set; } = new();

    public List<CategorySeed> Categories { get; set; } = new();
}

public sealed class CategorySeed
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<MenuItemSeed> Items { get; set; } = new();
}

public sealed class MenuItemSeed
{
    public string Title { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
}
