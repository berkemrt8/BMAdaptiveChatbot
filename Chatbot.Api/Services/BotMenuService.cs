using Chatbot.Api.Data;
using Chatbot.Api.Dtos;
using Chatbot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Api.Services;

// Botun yardım menüsünü, alias'larını ve takip ifadelerini veritabanından okur.
public class BotMenuService
{
    private readonly AppDbContext _db;

    public BotMenuService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HelpMenuDto> LoadAsync(BotProfile bot, CancellationToken cancellationToken = default)
    {
        var categories = await _db.HelpCategories
            .AsNoTracking()
            .Where(category => category.BotProfileId == bot.Id)
            .OrderBy(category => category.SortOrder)
            .Select(category => new HelpCategoryDto
            {
                Title = category.Title,
                Icon = category.Icon,
                Prompt = category.Prompt,
                Aliases = category.Aliases.Select(alias => alias.Phrase).ToList(),
                Items = category.Items
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new HelpOptionDto
                    {
                        Title = item.Title,
                        Intent = item.IntentName,
                        ActionText = item.ActionText,
                        ActionUrl = item.ActionUrl,
                        Aliases = item.Aliases.Select(alias => alias.Phrase).ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var followUpPhrases = await _db.FollowUpPhrases
            .AsNoTracking()
            .Where(phrase => phrase.BotProfileId == bot.Id)
            .Select(phrase => phrase.Phrase)
            .ToListAsync(cancellationToken);

        return new HelpMenuDto
        {
            Prompt = bot.MenuPrompt,
            Categories = categories,
            FollowUpPhrases = followUpPhrases
        };
    }
}
