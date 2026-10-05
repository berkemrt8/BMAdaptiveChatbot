using Chatbot.Api.Services.ExternalData;

namespace Chatbot.Tests;

// "5000 TL altında klavye" gibi mesajlardan bütçenin okunması (NexoraCatalogProvider).
public class BudgetParsingTests
{
    [Theory]
    [InlineData("5000 TL altında klavye", 5000)]
    [InlineData("5.000 lira bütçem var", 5000)]
    [InlineData("5 bin liraya kadar kulaklık", 5000)]
    [InlineData("2,5 bin TL", 2500)]
    [InlineData("12.500 TL altı monitör", 12500)]
    [InlineData("5000", 5000)]   // bütçe sorulduktan sonra yalnızca sayı yazılabilir
    public void Butce_mesajdan_okunur(string message, int expected)
    {
        Assert.Equal(expected, NexoraCatalogProvider.TryExtractBudget(message));
    }

    [Theory]
    [InlineData("ucuz bir klavye öner")]
    [InlineData("2 tb ssd")]         // sayı var ama para birimi yok
    [InlineData("")]
    public void Butce_yoksa_null_doner(string message)
    {
        Assert.Null(NexoraCatalogProvider.TryExtractBudget(message));
    }
}
