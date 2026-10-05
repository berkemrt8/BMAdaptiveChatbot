namespace Chatbot.Api.Models;

public class BotProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = string.Empty;
    public string FallbackMessage { get; set; } = string.Empty;

    // Ana menünün başlığı (örn. "Size hangi konuda yardımcı olabilirim?").
    public string MenuPrompt { get; set; } = string.Empty;

    public List<KnowledgeArticle> KnowledgeArticles { get; set; } = new();
    public List<ConversationMessage> ConversationMessages { get; set; } = new();
    public List<HelpCategory> HelpCategories { get; set; } = new();
    public List<FollowUpPhrase> FollowUpPhrases { get; set; } = new();
}
