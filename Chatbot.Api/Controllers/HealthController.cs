using Chatbot.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    // GET /api/health?botKey=nexora-demo -> o bot'un modeli hazır mı, hangi eşiklerle çalışıyor?
    // botKey verilmezse yalnızca API'nin ayakta olduğu döner.
    [HttpGet]
    public IActionResult Get([FromServices] IntentService intentService, [FromQuery] string? botKey)
    {
        if (string.IsNullOrWhiteSpace(botKey))
            return Ok(new { status = "ok" });

        var thresholds = intentService.GetThresholds(botKey);

        return Ok(new
        {
            status = "ok",
            botKey,
            modelReady = thresholds is not null,
            modelTrainedAtUtc = thresholds?.TrainedAtUtc,
            thresholds = thresholds is null ? null : new
            {
                thresholds.MinimumScore,
                thresholds.MinimumMargin,
                thresholds.SingleWordMinimumScore,
                thresholds.SingleWordMinimumMargin,
                thresholds.PairMinimumMass
            }
        });
    }
}
