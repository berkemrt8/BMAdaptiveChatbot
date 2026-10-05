using System.Globalization;
using System.Text;

namespace Chatbot.Api.Services;

// ML modeli bu normalizasyonla eğitildiği için Chatbot.Trainer'daki NormalizeText ile birebir aynı kalmalıdır.
public static class TextNormalizer
{
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text.Trim().ToLower(TurkishCulture)
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');
        var builder = new StringBuilder(lower.Length);

        foreach (var character in lower)
        {
            builder.Append(char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)
                ? character
                : ' ');
        }

        return string.Join(' ', builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static int CountWords(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    // İfade mesajın içinde tam kelime/kelime grubu olarak geçiyor mu? ("ev" -> "evet" eşleşmez)
    public static bool ContainsWholePhrase(string normalizedMessage, string phrase)
    {
        var normalizedPhrase = Normalize(phrase);

        if (string.IsNullOrWhiteSpace(normalizedPhrase))
            return false;

        return $" {normalizedMessage} ".Contains($" {normalizedPhrase} ", StringComparison.Ordinal);
    }
}
