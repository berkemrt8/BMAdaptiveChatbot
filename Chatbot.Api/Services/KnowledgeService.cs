using Chatbot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Api.Services;

public class KnowledgeService
{
    private readonly AppDbContext _db;

    public KnowledgeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetAnswerAsync(int botId, string intentName)
    {
        return await _db.KnowledgeArticles
            .Where(x => x.BotProfileId == botId && x.IntentName == intentName)
            .Select(x => x.Answer)
            .FirstOrDefaultAsync();
    }
}
