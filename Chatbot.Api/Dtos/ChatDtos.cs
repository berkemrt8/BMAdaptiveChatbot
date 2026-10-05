namespace Chatbot.Api.Dtos;

public class ChatRequest
{
    public string BotKey { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;

    // Menüden bir seçenek seçildiyse intent zaten bellidir, tahmin yapılmaz.
    public string? SelectedIntent { get; set; }
}

public class ChatResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    // Cevabın türü; istemci neyi göstereceğine buna bakarak karar verir. Değerler: ReplyTypes.
    public string Type { get; set; } = ReplyTypes.Answer;

    // Cevaplanan intent (Type answer veya question ise dolu).
    public string? Intent { get; set; }

    // Kararın hangi adımda verildiği. Değerler: ReplySources.
    public string Source { get; set; } = string.Empty;

    // Type options ise kullanıcıya buton olarak sunulacak seçenekler.
    public List<ChatOption>? Options { get; set; }

    // Cevap bir sayfaya yönlendirebiliyorsa (örn. "Şifre değiştirme sayfasını aç").
    public ChatActionDto? Action { get; set; }

    // Geliştirme sırasında test arayüzünde gösterilen model bilgileri.
    public ChatDebugInfo Debug { get; set; } = new();
}

public static class ReplyTypes
{
    public const string Answer = "answer";      // soru cevaplandı
    public const string Question = "question";  // bot cevap verebilmek için bir bilgi istiyor (örn. ürün adı)
    public const string Options = "options";    // bot emin değil, seçeneklerden birini bekliyor
    public const string Menu = "menu";          // mesaj birden fazla konuya uyuyor, ana menü gösterilmeli
    public const string Fallback = "fallback";  // mesaj anlaşılamadı
}

public static class ReplySources
{
    public const string MenuButton = "menu";
    public const string ExactQuestion = "exact-question";
    public const string Alias = "alias";
    public const string PendingInput = "pending-input";
    public const string Model = "model";
    public const string FollowUp = "follow-up";
    public const string TopTwo = "top-two";
    public const string Category = "category";
    public const string CandidateCategory = "candidate-category";
    public const string Fallback = "fallback";
}

public class ChatOption
{
    public string Title { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
}

public class ChatActionDto
{
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ChatDebugInfo
{
    // ML modeli bu mesaj için çalıştıysa ilk iki tahmini ve skorları.
    public string? CandidateIntent { get; set; }
    public float? Score { get; set; }
    public float? Margin { get; set; }
    public string? SecondIntent { get; set; }
    public float? SecondScore { get; set; }

    // Cevap harici veri sağlayıcıdan (örn. Nexora ürün servisi) başarıyla geldi.
    public bool UsedExternalData { get; set; }
}
