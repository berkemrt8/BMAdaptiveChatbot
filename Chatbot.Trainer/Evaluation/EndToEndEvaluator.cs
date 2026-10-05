using System.Net.Http.Json;
using System.Text.Json;

namespace Chatbot.Trainer.Evaluation;

// Regression sorularını çalışan Chatbot.Api'ye gönderir. Böylece yalnızca ML modeli değil,
// menü/alias/netleştirme katmanlarıyla birlikte kullanıcının gerçekte gördüğü sonuç ölçülür.
public static class EndToEndEvaluator
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<EndToEndReport?> RunAsync(
        Uri apiBaseUrl,
        string botKey,
        IReadOnlyList<(string Text, string Label)> rows,
        DateTime? expectedTrainedAtUtc)
    {
        using var client = new HttpClient { BaseAddress = apiBaseUrl, Timeout = TimeSpan.FromSeconds(15) };

        if (!await WaitForModelAsync(client, botKey, expectedTrainedAtUtc))
            return null;

        var report = new EndToEndReport { BotKey = botKey, RunAtUtc = DateTime.UtcNow, Total = rows.Count };

        foreach (var (text, label) in rows)
        {
            // Her soru ayrı oturumda sorulur ki önceki cevap bağlamı sonucu etkilemesin.
            // "eval-" öneki bu kayıtların konuşma loglarında ayırt edilebilmesi için.
            var request = new { botKey, sessionId = $"eval-{Guid.NewGuid():N}", message = text };
            using var response = await client.PostAsJsonAsync("api/chat", request);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var outcome = Classify(json.RootElement, label);
            report.Add(outcome);

            if (outcome is Outcome.WrongAnswer or Outcome.Fallback)
            {
                var intent = json.RootElement.GetProperty("intent").GetString() ?? json.RootElement.GetProperty("type").GetString();
                report.Problems.Add($"{(outcome == Outcome.WrongAnswer ? "yanlış   " : "fallback ")} | {text} | beklenen {label} | sonuç {intent}");
            }
        }

        return report;
    }

    public static EndToEndReport? LoadPrevious(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<EndToEndReport>(File.ReadAllText(path), ReportJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(EndToEndReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, ReportJson));
    }

    public static void Print(EndToEndReport current, EndToEndReport? previous)
    {
        Console.WriteLine($"{"",-26} {"Önceki",8} {"Şimdi",8} {"Fark",8}");
        PrintLine("Doğru cevap", r => r.Correct, current, previous, higherIsBetter: true);
        PrintLine("Doğru seçenek sunuldu", r => r.RightSuggestion, current, previous, higherIsBetter: true);
        PrintLine("Ana menü gösterildi", r => r.MainMenu, current, previous, higherIsBetter: null);
        PrintLine("Yanlış seçenek sunuldu", r => r.WrongSuggestion, current, previous, higherIsBetter: false);
        PrintLine("Yanlış cevap", r => r.WrongAnswer, current, previous, higherIsBetter: false);
        PrintLine("Fallback", r => r.Fallback, current, previous, higherIsBetter: false);

        if (previous is not null)
            Console.WriteLine($"(önceki ölçüm: {previous.RunAtUtc.ToLocalTime():dd.MM.yyyy HH:mm})");

        if (current.Problems.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Yanlış cevaplar ve fallback'ler:");
            foreach (var line in current.Problems.Take(25))
                Console.WriteLine($"- {line}");

            if (current.Problems.Count > 25)
                Console.WriteLine($"... {current.Problems.Count - 25} tane daha.");
        }
    }

    private static void PrintLine(
        string title,
        Func<EndToEndReport, int> selector,
        EndToEndReport current,
        EndToEndReport? previous,
        bool? higherIsBetter)
    {
        var now = current.Rate(selector(current));
        var before = previous is null ? (double?)null : previous.Rate(selector(previous));
        var diff = before is null ? "" : $"{(now - before.Value) * 100:+0.0;-0.0;0.0}";
        var mark = before is null || higherIsBetter is null || Math.Abs(now - before.Value) < 0.0005
            ? ""
            : (now > before.Value) == higherIsBetter ? " ✓" : " ✗";

        Console.WriteLine($"{title,-26} {Format(before),8} {Format(now),8} {diff,8}{mark}");
    }

    private static string Format(double? rate) => rate is null ? "-" : $"%{rate.Value * 100:0.0}";

    private static Outcome Classify(JsonElement response, string expectedIntent)
    {
        switch (response.GetProperty("type").GetString())
        {
            case "answer":
            case "question":
                return response.GetProperty("intent").GetString() == expectedIntent
                    ? Outcome.Correct
                    : Outcome.WrongAnswer;

            case "options":
                // Kullanıcı sunulan butonlardan birine tıklayarak doğru cevaba ulaşabiliyor mu?
                var offered = response.GetProperty("options")
                    .EnumerateArray()
                    .Select(option => option.GetProperty("intent").GetString());
                return offered.Contains(expectedIntent) ? Outcome.RightSuggestion : Outcome.WrongSuggestion;

            case "menu":
                return Outcome.MainMenu;

            default:
                return Outcome.Fallback;
        }
    }

    // Trainer modeli yeni kaydettiyse API'nin onu yüklemesini bekler (API model dosyası değişince kendisi yükler).
    private static async Task<bool> WaitForModelAsync(HttpClient client, string botKey, DateTime? expectedTrainedAtUtc)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (true)
        {
            try
            {
                using var health = JsonDocument.Parse(await client.GetStringAsync($"api/health?botKey={Uri.EscapeDataString(botKey)}"));

                if (expectedTrainedAtUtc is null)
                    return true;

                if (health.RootElement.TryGetProperty("modelTrainedAtUtc", out var trainedAt)
                    && trainedAt.ValueKind == JsonValueKind.String
                    && Math.Abs((trainedAt.GetDateTime().ToUniversalTime() - expectedTrainedAtUtc.Value).TotalSeconds) < 1)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                Console.WriteLine($"Chatbot API'ye ulaşılamadı ({client.BaseAddress}). Uçtan uca test atlandı.");
                Console.WriteLine("API'yi başlattıktan sonra yalnızca bu testi çalıştırmak için: -- <botKey> --e2e-only");
                return false;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"Chatbot API yanıt vermedi ({client.BaseAddress}). Uçtan uca test atlandı.");
                return false;
            }

            if (DateTime.UtcNow > deadline)
            {
                Console.WriteLine("API çalışıyor ama yeni modeli yüklemedi (eski bir API sürümü çalışıyor olabilir).");
                Console.WriteLine("API'yi yeniden başlatıp şunu çalıştırın: -- <botKey> --e2e-only");
                return false;
            }

            await Task.Delay(500);
        }
    }
}

public enum Outcome
{
    Correct,
    RightSuggestion,
    MainMenu,
    WrongSuggestion,
    WrongAnswer,
    Fallback
}

public sealed class EndToEndReport
{
    public string BotKey { get; set; } = string.Empty;
    public DateTime RunAtUtc { get; set; }
    public int Total { get; set; }
    public int Correct { get; set; }
    public int RightSuggestion { get; set; }
    public int MainMenu { get; set; }
    public int WrongSuggestion { get; set; }
    public int WrongAnswer { get; set; }
    public int Fallback { get; set; }
    public List<string> Problems { get; set; } = new();

    public double Rate(int count) => Total == 0 ? 0 : (double)count / Total;

    public void Add(Outcome outcome)
    {
        switch (outcome)
        {
            case Outcome.Correct: Correct++; break;
            case Outcome.RightSuggestion: RightSuggestion++; break;
            case Outcome.MainMenu: MainMenu++; break;
            case Outcome.WrongSuggestion: WrongSuggestion++; break;
            case Outcome.WrongAnswer: WrongAnswer++; break;
            case Outcome.Fallback: Fallback++; break;
        }
    }
}
