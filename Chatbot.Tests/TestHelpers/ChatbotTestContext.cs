using Chatbot.Api.Data;
using Chatbot.Api.Dtos;
using Chatbot.Api.Models;
using Chatbot.Api.Services;
using Chatbot.Api.Services.ExternalData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Chatbot.Tests.TestHelpers;

// Her test için bellekte çalışan (SQLite in-memory) boş bir veritabanı ve küçük bir test botu kurar.
// SQL Server gerekmez; test bitince veritabanı yok olur.
public sealed class ChatbotTestContext : IDisposable
{
    public const string BotKey = "test-bot";
    public const string FallbackMessage = "Bu soruyu anlayamadım.";

    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }
    public FakeIntentClassifier Classifier { get; } = new();
    public FakeProductProvider Provider { get; } = new();
    public ChatbotService Service { get; }

    public ChatbotTestContext()
    {
        // In-memory SQLite veritabanı bağlantı açık kaldığı sürece yaşar.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();
        SeedTestBot();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ExternalData:BotProviders:{BotKey}"] = FakeProductProvider.ProviderName
            })
            .Build();

        Service = new ChatbotService(
            Db,
            Classifier,
            new KnowledgeService(Db),
            new BotMenuService(Db),
            new ExternalDataProviderRegistry([Provider], configuration));
    }

    public Task<ChatResponse> SendAsync(string message, string? sessionId = null, string? selectedIntent = null) =>
        Service.SendAsync(new ChatRequest
        {
            BotKey = BotKey,
            Message = message,
            SessionId = sessionId,
            SelectedIntent = selectedIntent
        });

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }

    private void SeedTestBot()
    {
        var bot = new BotProfile
        {
            PublicKey = BotKey,
            Name = "Test Otel",
            WelcomeMessage = "Merhaba!",
            FallbackMessage = FallbackMessage,
            MenuPrompt = "Size nasıl yardımcı olabilirim?",
            FollowUpPhrases = [new() { Phrase = "kaçta" }, new() { Phrase = "bitişi" }],
            KnowledgeArticles =
            [
                Answer("BreakfastTime", "Kahvaltı 07:00-10:30 arasındadır."),
                Answer("BreakfastIncluded", "Kahvaltı oda fiyatına dahildir."),
                Answer("CheckInTime", "Check-in saati 14:00'tür."),
                Answer("CheckOutTime", "Check-out saati 11:00'dir."),
                Answer("PaymentMethods", "Kredi kartı ve nakit kabul edilir."),
                Answer("CancellationPolicy", "48 saat öncesine kadar iptal ücretsizdir.")
            ],
            HelpCategories =
            [
                Category("Yeme ve İçme", ["kahvaltı", "restoran"],
                    Item("BreakfastTime", "Kahvaltı saatleri nedir?", "kahvaltı kaçta"),
                    Item("BreakfastIncluded", "Kahvaltı fiyata dahil mi?", "kahvaltı dahil")),
                Category("Giriş ve Çıkış", ["giriş", "çıkış"],
                    Item("CheckInTime", "Check-in saati kaç?", "giriş saati"),
                    Item("CheckOutTime", "Check-out saati kaç?", "çıkış saati")),
                Category("Ödeme ve İptal", ["ödeme", "iptal"],
                    Item("PaymentMethods", "Hangi ödeme yöntemleri geçerli?", "kredi kartı"),
                    Item("CancellationPolicy", "İptal koşulları nelerdir?", "iptal koşulları")),
                // ProductPrice'ın cevabı veritabanında değil, sahte sağlayıcıdan gelir.
                Category("Ürünler", ["ürün"],
                    Item("ProductPrice", "Bir ürünün fiyatını öğrenmek istiyorum"))
            ]
        };

        // Menü sırası belirli olsun ki seçeneklerin sırasını kontrol eden testler kararlı çalışsın.
        for (var i = 0; i < bot.HelpCategories.Count; i++)
            bot.HelpCategories[i].SortOrder = i;

        Db.BotProfiles.Add(bot);
        Db.SaveChanges();
    }

    private static KnowledgeArticle Answer(string intent, string text) =>
        new() { IntentName = intent, Answer = text };

    private static HelpCategory Category(string title, string[] aliases, params MenuItem[] items) => new()
    {
        Title = title,
        Prompt = $"{title} hakkında ne öğrenmek istersiniz?",
        Aliases = aliases.Select(phrase => new HelpCategoryAlias { Phrase = phrase }).ToList(),
        Items = items.Select((item, index) =>
        {
            item.SortOrder = index;
            return item;
        }).ToList()
    };

    private static MenuItem Item(string intent, string title, params string[] aliases) => new()
    {
        IntentName = intent,
        Title = title,
        Aliases = aliases.Select(phrase => new MenuItemAlias { Phrase = phrase }).ToList()
    };
}
