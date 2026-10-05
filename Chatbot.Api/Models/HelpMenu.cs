namespace Chatbot.Api.Models;

// Botun yardım menüsü ve mesaj eşleştirmede kullanılan ifadeler.
//
// BotProfile ──< HelpCategory ──< MenuItem ──< MenuItemAlias
//      │              └──< HelpCategoryAlias
//      └──< FollowUpPhrase

// Menüdeki bir konu başlığı (örn. "Rezervasyon ve Odalar").
public class HelpCategory
{
    public int Id { get; set; }
    public int BotProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    // Kategori seçildiğinde gösterilen soru (örn. "Rezervasyon konusunda ne öğrenmek istersiniz?").
    public string Prompt { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public BotProfile BotProfile { get; set; } = null!;
    public List<HelpCategoryAlias> Aliases { get; set; } = new();
    public List<MenuItem> Items { get; set; } = new();
}

// Mesajda geçtiğinde bu konuyu işaret eden kelime (örn. "kargo", "hesap").
public class HelpCategoryAlias
{
    public int Id { get; set; }
    public int HelpCategoryId { get; set; }
    public string Phrase { get; set; } = string.Empty;

    public HelpCategory HelpCategory { get; set; } = null!;
}

// Menüde buton olarak görünen soru. Seçildiğinde IntentName'e ait cevap verilir.
public class MenuItem
{
    public int Id { get; set; }
    public int HelpCategoryId { get; set; }
    public string IntentName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    // Cevabın altında gösterilecek bağlantı (örn. "Şifre değiştirme sayfasını aç" -> /Account/ChangePassword).
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
    public int SortOrder { get; set; }

    public HelpCategory HelpCategory { get; set; } = null!;
    public List<MenuItemAlias> Aliases { get; set; } = new();
}

// Mesajda geçtiğinde ML'e sormadan doğrudan bu maddenin intent'ini seçtiren kesin ifade.
public class MenuItemAlias
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public string Phrase { get; set; } = string.Empty;

    public MenuItem MenuItem { get; set; } = null!;
}

// "peki bitişi?" gibi kısa takip sorularını tanımak için bota özel ifade.
public class FollowUpPhrase
{
    public int Id { get; set; }
    public int BotProfileId { get; set; }
    public string Phrase { get; set; } = string.Empty;

    public BotProfile BotProfile { get; set; } = null!;
}
