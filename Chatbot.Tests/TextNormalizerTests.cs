using Chatbot.Api.Services;

namespace Chatbot.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Şifremi UNUTTUM!", "sifremi unuttum")]
    [InlineData("  Kahvaltı   kaçta?  ", "kahvalti kacta")]
    [InlineData("İptal / iade", "iptal iade")]
    [InlineData("Check-in saati", "check in saati")]
    [InlineData("ĞÜŞİÖÇ ğüşıöç", "gusioc gusioc")]
    [InlineData("", "")]
    public void Normalize_turkce_karakterleri_ve_noktalamayi_sadelestirir(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("kahvalti kacta basliyor", "kahvaltı kaçta", true)]
    [InlineData("oda fiyati ne kadar", "oda fiyatı", true)]
    [InlineData("evet tamam", "ev", false)]        // kelimenin bir parçası eşleşme sayılmaz
    [InlineData("havuza girebilir miyim", "havuz", false)]
    [InlineData("kahvalti", "", false)]
    public void ContainsWholePhrase_yalnizca_tam_kelime_eslesmesini_kabul_eder(string normalizedMessage, string phrase, bool expected)
    {
        Assert.Equal(expected, TextNormalizer.ContainsWholePhrase(normalizedMessage, phrase));
    }

    [Theory]
    [InlineData("merhaba", 1)]
    [InlineData("peki bitişi?", 2)]
    [InlineData("  ", 0)]
    public void CountWords_normalize_edilmis_kelime_sayisini_verir(string input, int expected)
    {
        Assert.Equal(expected, TextNormalizer.CountWords(input));
    }
}
