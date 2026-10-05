namespace Chatbot.Api.Services;

public class BotNotFoundException : Exception
{
    public BotNotFoundException(string botKey)
        : base($"'{botKey}' anahtarlı bot bulunamadı.")
    {
    }
}
