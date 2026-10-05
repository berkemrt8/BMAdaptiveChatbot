using Chatbot.Api.Data;
using Chatbot.Api.Dtos;
using Chatbot.Api.Ml;
using Chatbot.Api.Models;
using Chatbot.Api.Services.ExternalData;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Api.Services;

// Bir mesaja nasıl cevap verileceğine karar verir.
//
// Karar sırayla denenen küçük adımlardan oluşur; ilk sonuç veren adım kazanır:
//   1. Menü butonu        : kullanıcı bir seçeneğe tıkladı, intent zaten belli
//   2. Birebir menü sorusu: mesaj bir menü sorusunun aynısı
//   3. Alias              : mesajda tek bir intent'e ait kesin bir ifade geçiyor
//   4. Bekleyen girdi     : bot önceki cevapta bir bilgi istemişti (örn. "Hangi ürün?")
//   5. Emin model         : ML.NET kalibre eşiğin üstünde bir tahmin yaptı
//   6. Takip sorusu       : "peki bitişi?" gibi önceki konuya dönen kısa mesaj
//   7. İki aday           : model iki intent arasında kaldı, ikisi seçenek olarak sunulur
//   8. Kategori kelimesi  : mesaj bir konuyu işaret ediyor, o konunun seçenekleri sunulur
//   9. Adayın kategorisi  : model emin değil ama tek bir aday öne çıkıyor, onun konusu sunulur
//  10. Fallback           : hiçbiri olmadı
public class ChatbotService
{
    // 9. adımın eşikleri. Diğer eşiklerden farklı olarak henüz trainer tarafından kalibre edilmiyor.
    private const float CandidateCategoryMinimumScore = 0.50f;
    private const float CandidateCategoryMinimumMargin = 0.10f;

    // ConversationMessages.DetectedIntent kolonuna yazılan özel değerler.
    // Oturum geçmişi bu kolondan okunduğu için değerler değiştirilmemelidir.
    private const string FallbackLog = "Fallback";
    private const string ClarificationPrefix = "Clarification:";
    private const string AwaitingInputPrefix = "AwaitingInput:";

    private readonly AppDbContext _db;
    private readonly IIntentClassifier _intentService;
    private readonly KnowledgeService _knowledgeService;
    private readonly BotMenuService _menuService;
    private readonly ExternalDataProviderRegistry _providerRegistry;

    public ChatbotService(
        AppDbContext db,
        IIntentClassifier intentService,
        KnowledgeService knowledgeService,
        BotMenuService menuService,
        ExternalDataProviderRegistry providerRegistry)
    {
        _db = db;
        _intentService = intentService;
        _knowledgeService = knowledgeService;
        _menuService = menuService;
        _providerRegistry = providerRegistry;
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var bot = await _db.BotProfiles.FirstOrDefaultAsync(x => x.PublicKey == request.BotKey, cancellationToken)
            ?? throw new BotNotFoundException(request.BotKey);

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString("N") : request.SessionId;
        var provider = _providerRegistry.GetForBot(bot.PublicKey);
        var history = await GetRecentIntentsAsync(bot.Id, sessionId, cancellationToken);
        var message = request.Message.Trim();

        var turn = new Turn
        {
            Bot = bot,
            Menu = await _menuService.LoadAsync(bot, cancellationToken),
            Provider = provider,
            SessionId = sessionId,
            Message = message,
            NormalizedMessage = TextNormalizer.Normalize(message),
            SelectedIntent = string.IsNullOrWhiteSpace(request.SelectedIntent) ? null : request.SelectedIntent,
            PendingIntent = GetPendingIntent(history.FirstOrDefault(), provider),
            LastAnsweredIntent = history.FirstOrDefault(IsAnsweredIntent)
        };

        var decision = Decide(turn);
        var reply = await BuildReplyAsync(turn, decision, cancellationToken);

        _db.ConversationMessages.Add(new ConversationMessage
        {
            BotProfileId = bot.Id,
            SessionId = sessionId,
            Role = "User",
            Message = request.Message
        });

        _db.ConversationMessages.Add(new ConversationMessage
        {
            BotProfileId = bot.Id,
            SessionId = sessionId,
            Role = "Assistant",
            Message = reply.Message,
            DetectedIntent = ToLogValue(reply, decision),
            ModelScore = turn.Prediction?.ModelScore
        });

        await _db.SaveChangesAsync(cancellationToken);
        return reply;
    }

    // ---------------------------------------------------------------- karar zinciri

    private Decision Decide(Turn turn)
    {
        // Menüden gelen seçimde tahmin yapılmaz; intent geçersizse cevap aşamasında fallback olur.
        if (turn.SelectedIntent is not null)
            return Decision.Answer(turn.SelectedIntent, ReplySources.MenuButton);

        return FromExactQuestion(turn)
            ?? FromAlias(turn)
            ?? FromPendingInput(turn)
            ?? FromConfidentModel(turn)
            ?? FromFollowUp(turn)
            ?? FromTopTwoCandidates(turn)
            ?? FromCategoryWords(turn)
            ?? FromCandidateCategory(turn)
            ?? Decision.Fallback;
    }

    private static Decision? FromExactQuestion(Turn turn)
    {
        var item = MenuItems(turn.Menu)
            .FirstOrDefault(x => TextNormalizer.Normalize(x.Title) == turn.NormalizedMessage);

        return item is null ? null : Decision.Answer(item.Intent, ReplySources.ExactQuestion);
    }

    private static Decision? FromAlias(Turn turn)
    {
        var intents = MenuItems(turn.Menu)
            .Where(item => item.Aliases.Any(alias => TextNormalizer.ContainsWholePhrase(turn.NormalizedMessage, alias)))
            .Select(item => item.Intent)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Mesaj birden fazla intent'in alias'ına uyuyorsa tahmin yürütmüyoruz.
        return intents.Count == 1 ? Decision.Answer(intents[0], ReplySources.Alias) : null;
    }

    private Decision? FromPendingInput(Turn turn)
    {
        if (turn.PendingIntent is null)
            return null;

        // Model mesajın başka, sabit cevaplı bir konuya (kargo, iade...) ait olduğundan eminse
        // kullanıcı konuyu değiştirmiştir; değilse bu mesaj istenen bilginin kendisidir.
        var prediction = Predict(turn);
        if (prediction.IsConfident && turn.Provider?.CanHandle(prediction.IntentName) != true)
            return Decision.Answer(prediction.IntentName, ReplySources.Model);

        return Decision.Answer(turn.PendingIntent, ReplySources.PendingInput);
    }

    private Decision? FromConfidentModel(Turn turn)
    {
        var prediction = Predict(turn);
        return prediction.IsConfident ? Decision.Answer(prediction.IntentName, ReplySources.Model) : null;
    }

    private static Decision? FromFollowUp(Turn turn)
    {
        if (turn.LastAnsweredIntent is null || !LooksLikeFollowUp(turn.Menu, turn.NormalizedMessage))
            return null;

        return Decision.Answer(turn.LastAnsweredIntent, ReplySources.FollowUp);
    }

    private Decision? FromTopTwoCandidates(Turn turn)
    {
        var prediction = Predict(turn);
        if (!prediction.CanOfferTopTwo)
            return null;

        // İki adayın da menüde butonda gösterilecek bir başlığı olmalı.
        var first = FindMenuItem(turn.Menu, prediction.IntentName);
        var second = FindMenuItem(turn.Menu, prediction.SecondIntentName);
        if (first is null || second is null)
            return null;

        return Decision.Choose(
            [first, second],
            "Şunlardan hangisini sormak istediniz?",
            ReplySources.TopTwo,
            logLabel: "TopCandidates");
    }

    private static Decision? FromCategoryWords(Turn turn)
    {
        var categories = turn.Menu?.Categories
            .Where(category => category.Aliases
                .Concat(category.Items.SelectMany(item => item.Aliases))
                .Any(alias => TextNormalizer.ContainsWholePhrase(turn.NormalizedMessage, alias)))
            .ToList() ?? [];

        var topic = turn.Message.TrimEnd('.', ',', '?', '!', ';', ':');
        var message = $"{topic} ile ilgili hangi konuda yardım edebilirim?";

        return categories.Count switch
        {
            0 => null,
            1 => Decision.Choose(categories[0].Items, message, ReplySources.Category, logLabel: categories[0].Title),
            _ => Decision.MainMenu(message)
        };
    }

    private Decision? FromCandidateCategory(Turn turn)
    {
        var prediction = Predict(turn);
        if (prediction.ModelScore < CandidateCategoryMinimumScore || prediction.ScoreMargin < CandidateCategoryMinimumMargin)
            return null;

        var category = turn.Menu?.Categories.FirstOrDefault(category => category.Items
            .Any(item => string.Equals(item.Intent, prediction.IntentName, StringComparison.OrdinalIgnoreCase)));

        if (category is null)
            return null;

        return Decision.Choose(
            category.Items,
            $"{category.Title} konusundan bahsediyorsanız aşağıdaki seçeneklerden birini seçebilirsiniz.",
            ReplySources.CandidateCategory,
            logLabel: category.Title);
    }

    // ---------------------------------------------------------------- cevabı oluşturma

    private async Task<ChatResponse> BuildReplyAsync(Turn turn, Decision decision, CancellationToken cancellationToken)
    {
        var reply = new ChatResponse
        {
            SessionId = turn.SessionId,
            Source = decision.Source,
            Debug = BuildDebugInfo(turn.Prediction)
        };

        if (decision.Options is not null)
        {
            reply.Type = ReplyTypes.Options;
            reply.Message = decision.Message!;
            reply.Options = decision.Options
                .Select(x => new ChatOption { Title = x.Title, Intent = x.Intent })
                .ToList();
            return reply;
        }

        if (decision.ShowMainMenu)
        {
            reply.Type = ReplyTypes.Menu;
            reply.Message = decision.Message!;
            return reply;
        }

        if (decision.Intent is null)
            return AsFallback(reply, turn.Bot);

        // Canlı veri gerektiren intent'ler (fiyat, stok...) sağlayıcıdan, diğerleri bilgi tabanından cevaplanır.
        if (turn.Provider?.CanHandle(decision.Intent) == true)
        {
            var external = await turn.Provider.GetAnswerAsync(new ExternalDataRequest
            {
                Intent = decision.Intent,
                Message = turn.Message,
                IsMenuSelection = decision.Source == ReplySources.MenuButton
            }, cancellationToken);

            reply.Type = external.AwaitingInput ? ReplyTypes.Question : ReplyTypes.Answer;
            reply.Message = external.Message;
            reply.Debug.UsedExternalData = !external.IsUnavailable;
        }
        else
        {
            var answer = await _knowledgeService.GetAnswerAsync(turn.Bot.Id, decision.Intent);
            if (answer is null)
                return AsFallback(reply, turn.Bot);

            reply.Type = ReplyTypes.Answer;
            reply.Message = answer;
        }

        reply.Intent = decision.Intent;
        reply.Action = FindAction(turn.Menu, decision.Intent);
        return reply;
    }

    private static ChatResponse AsFallback(ChatResponse reply, BotProfile bot)
    {
        reply.Type = ReplyTypes.Fallback;
        reply.Message = bot.FallbackMessage;
        reply.Intent = null;

        if (reply.Source != ReplySources.MenuButton)
            reply.Source = ReplySources.Fallback;

        return reply;
    }

    private static ChatDebugInfo BuildDebugInfo(IntentPredictionResult? prediction) => prediction is null
        ? new ChatDebugInfo()
        : new ChatDebugInfo
        {
            CandidateIntent = prediction.IntentName,
            Score = prediction.ModelScore,
            Margin = prediction.ScoreMargin,
            SecondIntent = prediction.SecondIntentName,
            SecondScore = prediction.SecondBestScore
        };

    private static ChatActionDto? FindAction(HelpMenuDto? menu, string intent)
    {
        var item = FindMenuItem(menu, intent);
        if (item is null || string.IsNullOrWhiteSpace(item.ActionUrl))
            return null;

        return new ChatActionDto
        {
            Text = string.IsNullOrWhiteSpace(item.ActionText) ? "İlgili sayfayı aç" : item.ActionText,
            Url = item.ActionUrl
        };
    }

    // ---------------------------------------------------------------- oturum geçmişi

    // Oturumdaki son asistan cevaplarının DetectedIntent değerleri, en yeniden eskiye.
    // Takip sorusu için yakın geçmiş yeterli olduğundan son 20 cevaba bakılır.
    private async Task<List<string?>> GetRecentIntentsAsync(int botId, string sessionId, CancellationToken cancellationToken)
    {
        return await _db.ConversationMessages
            .Where(x => x.BotProfileId == botId && x.SessionId == sessionId && x.Role == "Assistant")
            .OrderByDescending(x => x.Id)
            .Select(x => x.DetectedIntent)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    // Önceki cevap bir bilgi istediyse (örn. "AwaitingInput:ProductPrice") beklenen intent.
    private static string? GetPendingIntent(string? lastLogValue, IExternalDataProvider? provider)
    {
        if (provider is null || lastLogValue is null || !lastLogValue.StartsWith(AwaitingInputPrefix, StringComparison.Ordinal))
            return null;

        var intent = lastLogValue[AwaitingInputPrefix.Length..];
        return provider.CanHandle(intent) ? intent : null;
    }

    private static bool IsAnsweredIntent(string? logValue) =>
        !string.IsNullOrEmpty(logValue)
        && logValue != FallbackLog
        && !logValue.StartsWith(ClarificationPrefix, StringComparison.Ordinal)
        && !logValue.StartsWith(AwaitingInputPrefix, StringComparison.Ordinal);

    private static string ToLogValue(ChatResponse reply, Decision decision) => reply.Type switch
    {
        ReplyTypes.Answer => reply.Intent!,
        ReplyTypes.Question => AwaitingInputPrefix + reply.Intent,
        ReplyTypes.Options => ClarificationPrefix + decision.LogLabel,
        ReplyTypes.Menu => ClarificationPrefix + "MainMenu",
        _ => FallbackLog
    };

    // ---------------------------------------------------------------- yardımcılar

    // ML tahmini ancak bir adım ihtiyaç duyarsa ve mesaj başına bir kez yapılır.
    private IntentPredictionResult Predict(Turn turn) =>
        turn.Prediction ??= _intentService.Predict(turn.Message, turn.Bot.PublicKey);

    private static IEnumerable<HelpOptionDto> MenuItems(HelpMenuDto? menu) =>
        menu?.Categories.SelectMany(category => category.Items) ?? [];

    private static HelpOptionDto? FindMenuItem(HelpMenuDto? menu, string? intent) =>
        intent is null
            ? null
            : MenuItems(menu).FirstOrDefault(x => string.Equals(x.Intent, intent, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeFollowUp(HelpMenuDto? menu, string normalizedMessage)
    {
        if (TextNormalizer.CountWords(normalizedMessage) > 5)
            return false;

        // Dilden gelen genel takip kalıpları; bota özel ifadeler help-menu.json -> followUpPhrases içindedir.
        if (normalizedMessage == "ya"
            || normalizedMessage.StartsWith("ya ", StringComparison.Ordinal)
            || TextNormalizer.ContainsWholePhrase(normalizedMessage, "peki"))
        {
            return true;
        }

        return menu?.FollowUpPhrases.Any(phrase => TextNormalizer.ContainsWholePhrase(normalizedMessage, phrase)) == true;
    }

    // Bir mesajın işlenmesi boyunca gereken bilgiler.
    private sealed class Turn
    {
        public required BotProfile Bot { get; init; }
        public required HelpMenuDto? Menu { get; init; }
        public required IExternalDataProvider? Provider { get; init; }
        public required string SessionId { get; init; }
        public required string Message { get; init; }
        public string? SelectedIntent { get; init; }
        public string? PendingIntent { get; init; }
        public string? LastAnsweredIntent { get; init; }
        public required string NormalizedMessage { get; init; }
        public IntentPredictionResult? Prediction { get; set; }
    }

    // Karar zincirindeki bir adımın sonucu.
    private sealed class Decision
    {
        public required string Source { get; init; }
        public string? Intent { get; init; }
        public List<HelpOptionDto>? Options { get; init; }
        public string? Message { get; init; }
        public string? LogLabel { get; init; }
        public bool ShowMainMenu { get; init; }

        public static readonly Decision Fallback = new() { Source = ReplySources.Fallback };

        public static Decision Answer(string intent, string source) =>
            new() { Source = source, Intent = intent };

        public static Decision Choose(List<HelpOptionDto> options, string message, string source, string logLabel) =>
            new() { Source = source, Options = options, Message = message, LogLabel = logLabel };

        public static Decision MainMenu(string message) =>
            new() { Source = ReplySources.Category, Message = message, ShowMainMenu = true };
    }
}
