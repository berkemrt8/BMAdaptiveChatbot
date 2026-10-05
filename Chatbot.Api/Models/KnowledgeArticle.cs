namespace Chatbot.Api.Models;

public class KnowledgeArticle
{
    public int Id { get; set; }
    public int BotProfileId { get; set; }
    public string IntentName { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    public BotProfile BotProfile { get; set; } = null!;
}
