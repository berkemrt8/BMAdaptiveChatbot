using Chatbot.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Tests;

// Veritabanı asıl kaynaktır: seeder yalnızca boş olanı doldurmalı, dolu veriye dokunmamalı.
public class DbSeederTests : IDisposable
{
    private const string SeedJson = """
    {
      "botKey": "seed-bot",
      "name": "Seed Bot",
      "welcomeMessage": "Merhaba",
      "fallbackMessage": "Anlayamadım",
      "menuPrompt": "Size nasıl yardımcı olabilirim?",
      "followUpPhrases": ["kaçta"],
      "answers": {
        "CheckInTime": "Check-in 14:00.",
        "CheckOutTime": "Check-out 11:00."
      },
      "categories": [
        {
          "title": "Giriş ve Çıkış",
          "icon": "🧳",
          "prompt": "Ne öğrenmek istersiniz?",
          "aliases": ["giriş", "çıkış"],
          "items": [
            { "title": "Check-in saati kaç?", "intent": "CheckInTime", "aliases": ["giriş saati"] },
            { "title": "Check-out saati kaç?", "intent": "CheckOutTime", "aliases": ["çıkış saati"], "actionText": "Aç", "actionUrl": "/cikis" }
          ]
        }
      ]
    }
    """;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly string _seedDirectory;

    public DbSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _seedDirectory = Path.Combine(Path.GetTempPath(), $"chatbot-seed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_seedDirectory);
        File.WriteAllText(Path.Combine(_seedDirectory, "seed-bot.json"), SeedJson);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        Directory.Delete(_seedDirectory, recursive: true);
    }

    [Fact]
    public async Task Bos_veritabani_seed_dosyasindan_doldurulur()
    {
        await DbSeeder.SeedAsync(_db, _seedDirectory);

        var bot = await _db.BotProfiles.SingleAsync();
        Assert.Equal("seed-bot", bot.PublicKey);
        Assert.Equal("Size nasıl yardımcı olabilirim?", bot.MenuPrompt);
        Assert.Equal(2, await _db.KnowledgeArticles.CountAsync());
        Assert.Equal(1, await _db.HelpCategories.CountAsync());
        Assert.Equal(2, await _db.HelpCategoryAliases.CountAsync());
        Assert.Equal(2, await _db.MenuItems.CountAsync());
        Assert.Equal(2, await _db.MenuItemAliases.CountAsync());
        Assert.Equal(1, await _db.FollowUpPhrases.CountAsync());

        var checkOut = await _db.MenuItems.SingleAsync(x => x.IntentName == "CheckOutTime");
        Assert.Equal("/cikis", checkOut.ActionUrl);
        Assert.Equal(1, checkOut.SortOrder);
    }

    [Fact]
    public async Task Veritabaninda_yapilan_degisiklik_tekrar_seed_edilince_korunur()
    {
        await DbSeeder.SeedAsync(_db, _seedDirectory);

        var answer = await _db.KnowledgeArticles.SingleAsync(x => x.IntentName == "CheckInTime");
        answer.Answer = "Check-in 15:00'e alındı.";
        await _db.SaveChangesAsync();

        await DbSeeder.SeedAsync(_db, _seedDirectory);

        _db.ChangeTracker.Clear();
        var reloaded = await _db.KnowledgeArticles.SingleAsync(x => x.IntentName == "CheckInTime");
        Assert.Equal("Check-in 15:00'e alındı.", reloaded.Answer);
        Assert.Equal(2, await _db.KnowledgeArticles.CountAsync());
        Assert.Equal(1, await _db.BotProfiles.CountAsync());
        Assert.Equal(2, await _db.MenuItems.CountAsync());
    }

    [Fact]
    public async Task Olmayan_seed_klasoru_hata_vermez()
    {
        await DbSeeder.SeedAsync(_db, Path.Combine(_seedDirectory, "yok"));

        Assert.Equal(0, await _db.BotProfiles.CountAsync());
    }
}
