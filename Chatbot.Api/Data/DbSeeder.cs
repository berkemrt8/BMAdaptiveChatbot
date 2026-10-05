using Chatbot.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Chatbot.Api.Data;

// Veritabanı asıl kaynaktır. Seeder yalnızca eksik olanı doldurur:
//   - bot veritabanında yoksa oluşturulur,
//   - botun cevapları boşsa seed dosyasındaki cevaplar eklenir,
//   - botun menüsü boşsa kategoriler, maddeler ve ifadeler eklenir.
// Dolu olan hiçbir bölüme dokunulmaz; veritabanında yapılan düzenlemeler kalıcıdır.
public static class DbSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext db, string seedDirectory)
    {
        if (!Directory.Exists(seedDirectory))
            return;

        foreach (var file in Directory.GetFiles(seedDirectory, "*.json").Order())
        {
            var seed = JsonSerializer.Deserialize<BotSeed>(await File.ReadAllTextAsync(file), JsonOptions)
                ?? throw new InvalidOperationException($"{Path.GetFileName(file)} okunamadı.");

            await SeedBotAsync(db, seed);
        }
    }

    private static async Task SeedBotAsync(AppDbContext db, BotSeed seed)
    {
        var bot = await db.BotProfiles.FirstOrDefaultAsync(x => x.PublicKey == seed.BotKey);

        if (bot is null)
        {
            bot = new BotProfile
            {
                PublicKey = seed.BotKey,
                Name = seed.Name,
                WelcomeMessage = seed.WelcomeMessage,
                FallbackMessage = seed.FallbackMessage,
                MenuPrompt = seed.MenuPrompt
            };

            db.BotProfiles.Add(bot);
            await db.SaveChangesAsync();
        }

        if (!await db.KnowledgeArticles.AnyAsync(x => x.BotProfileId == bot.Id))
        {
            db.KnowledgeArticles.AddRange(seed.Answers.Select(answer => new KnowledgeArticle
            {
                BotProfileId = bot.Id,
                IntentName = answer.Key,
                Answer = answer.Value
            }));
        }

        if (!await db.HelpCategories.AnyAsync(x => x.BotProfileId == bot.Id))
        {
            // Menü tabloları eklenmeden önce oluşturulmuş botlarda menü başlığı da boştur.
            if (string.IsNullOrEmpty(bot.MenuPrompt))
                bot.MenuPrompt = seed.MenuPrompt;

            db.HelpCategories.AddRange(seed.Categories.Select((category, index) => ToEntity(bot.Id, category, index)));

            if (!await db.FollowUpPhrases.AnyAsync(x => x.BotProfileId == bot.Id))
            {
                db.FollowUpPhrases.AddRange(seed.FollowUpPhrases.Select(phrase => new FollowUpPhrase
                {
                    BotProfileId = bot.Id,
                    Phrase = phrase
                }));
            }
        }

        await db.SaveChangesAsync();
    }

    private static HelpCategory ToEntity(int botId, CategorySeed category, int sortOrder) => new()
    {
        BotProfileId = botId,
        Title = category.Title,
        Icon = category.Icon,
        Prompt = category.Prompt,
        SortOrder = sortOrder,
        Aliases = category.Aliases.Select(phrase => new HelpCategoryAlias { Phrase = phrase }).ToList(),
        Items = category.Items.Select((item, index) => new MenuItem
        {
            IntentName = item.Intent,
            Title = item.Title,
            ActionText = item.ActionText,
            ActionUrl = item.ActionUrl,
            SortOrder = index,
            Aliases = item.Aliases.Select(phrase => new MenuItemAlias { Phrase = phrase }).ToList()
        }).ToList()
    };
}
