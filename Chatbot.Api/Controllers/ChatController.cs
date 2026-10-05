using Chatbot.Api.Dtos;
using Chatbot.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ChatbotService _chatbotService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatbotService chatbotService, ILogger<ChatController> logger)
    {
        _chatbotService = chatbotService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Send(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BotKey) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "BotKey ve Message zorunludur." });
        }

        try
        {
            return Ok(await _chatbotService.SendAsync(request, cancellationToken));
        }
        catch (BotNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Model dosyası eksik, provider yapılandırılmamış gibi kurulum sorunları.
            _logger.LogWarning(ex, "Chatbot servisi kullanıma hazır değil. Bot: {BotKey}", request.BotKey);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ayrıntı yalnızca sunucu loguna yazılır; istemciye iç hata mesajı gönderilmez.
            _logger.LogError(ex, "Chat isteği işlenirken hata oluştu. Bot: {BotKey}", request.BotKey);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Chatbot isteği işlenirken bir hata oluştu."
            });
        }
    }
}
