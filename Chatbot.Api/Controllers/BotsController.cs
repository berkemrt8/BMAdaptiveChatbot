using Chatbot.Api.Data;
using Chatbot.Api.Dtos;
using Chatbot.Api.Services;
using Chatbot.Api.Services.ExternalData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/bots")]
public class BotsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BotMenuService _menuService;
    private readonly IntentService _intentService;
    private readonly ExternalDataProviderRegistry _providerRegistry;

    public BotsController(
        AppDbContext db,
        BotMenuService menuService,
        IntentService intentService,
        ExternalDataProviderRegistry providerRegistry)
    {
        _db = db;
        _menuService = menuService;
        _intentService = intentService;
        _providerRegistry = providerRegistry;
    }

    // GET /api/bots -> veritabanındaki bütün botlar (test arayüzündeki bot seçimi için)
    [HttpGet]
    public async Task<ActionResult<List<BotSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var bots = await _db.BotProfiles
            .AsNoTracking()
            .OrderBy(x => x.PublicKey)
            .Select(x => new { x.PublicKey, x.Name })
            .ToListAsync(cancellationToken);

        return Ok(bots.Select(bot => new BotSummaryDto
        {
            PublicKey = bot.PublicKey,
            Name = bot.Name,
            ModelReady = _intentService.GetThresholds(bot.PublicKey) is not null,
            DataProvider = _providerRegistry.GetForBot(bot.PublicKey)?.Name
        }).ToList());
    }

    // GET /api/bots/{botKey} -> botun adı, karşılama mesajı ve yardım menüsü
    [HttpGet("{botKey}")]
    public async Task<ActionResult<BotDto>> Get(string botKey, CancellationToken cancellationToken)
    {
        var bot = await _db.BotProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicKey == botKey, cancellationToken);

        if (bot is null)
            return NotFound(new { error = $"'{botKey}' anahtarlı bot bulunamadı." });

        return Ok(new BotDto
        {
            Name = bot.Name,
            PublicKey = bot.PublicKey,
            WelcomeMessage = bot.WelcomeMessage,
            HelpMenu = await _menuService.LoadAsync(bot, cancellationToken)
        });
    }
}
