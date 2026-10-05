namespace Chatbot.Api.Models;

public class ConversationMessage
{
    public int Id { get; set; }
    public int BotProfileId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DetectedIntent { get; set; }
    public float? ModelScore { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BotProfile BotProfile { get; set; } = null!;
}
