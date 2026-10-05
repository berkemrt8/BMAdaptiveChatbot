using Chatbot.Api.Dtos;
using Chatbot.Api.Services;
using Chatbot.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using static Chatbot.Tests.TestHelpers.FakeIntentClassifier;

namespace Chatbot.Tests;

// ChatbotService'in karar zincirini adım adım test eder.
// Her test yeni ve boş bir veritabanıyla başlar (xUnit her test için sınıfın yeni bir örneğini oluşturur).
public class ChatbotServiceTests : IDisposable
{
    private readonly ChatbotTestContext _context = new();

    public void Dispose() => _context.Dispose();

    // ---------------------------------------------------------------- menü, birebir soru, alias

    [Fact]
    public async Task Menu_butonu_secilirse_modele_sorulmadan_cevap_verilir()
    {
        var reply = await _context.SendAsync("Kahvaltı saatleri nedir?", selectedIntent: "BreakfastTime");

        Assert.Equal(ReplyTypes.Answer, reply.Type);
        Assert.Equal("BreakfastTime", reply.Intent);
        Assert.Equal(ReplySources.MenuButton, reply.Source);
        Assert.Equal(0, _context.Classifier.Calls);
    }

    [Fact]
    public async Task Gecersiz_bir_menu_secimi_fallback_olur()
    {
        var reply = await _context.SendAsync("?", selectedIntent: "BoyleBirIntentYok");

        Assert.Equal(ReplyTypes.Fallback, reply.Type);
        Assert.Equal(ChatbotTestContext.FallbackMessage, reply.Message);
    }

    [Fact]
    public async Task Menu_sorusu_birebir_yazilirsa_o_intent_secilir()
    {
        var reply = await _context.SendAsync("iptal koşulları nelerdir");

        Assert.Equal("CancellationPolicy", reply.Intent);
        Assert.Equal(ReplySources.ExactQuestion, reply.Source);
    }

    [Fact]
    public async Task Alias_eslesirse_modele_sorulmadan_cevap_verilir()
    {
        var reply = await _context.SendAsync("Kahvaltı kaçta başlıyor?");

        Assert.Equal("BreakfastTime", reply.Intent);
        Assert.Equal(ReplySources.Alias, reply.Source);
        Assert.Equal(0, _context.Classifier.Calls);
    }

    [Fact]
    public async Task Iki_farkli_intentin_aliasi_gecerse_alias_kullanilmaz()
    {
        // "giriş saati" CheckInTime'ın, "çıkış saati" CheckOutTime'ın alias'ı.
        _context.Classifier.Returns("giriş saati ve çıkış saati", Confident("CheckInTime"));

        var reply = await _context.SendAsync("giriş saati ve çıkış saati");

        Assert.Equal(ReplySources.Model, reply.Source);
    }

    // ---------------------------------------------------------------- model

    [Fact]
    public async Task Model_eminse_tahmin_edilen_intent_cevaplanir()
    {
        _context.Classifier.Returns("sabah yemeği saat kaçta veriliyor", Confident("BreakfastTime"));

        var reply = await _context.SendAsync("sabah yemeği saat kaçta veriliyor");

        Assert.Equal(ReplyTypes.Answer, reply.Type);
        Assert.Equal("BreakfastTime", reply.Intent);
        Assert.Equal(ReplySources.Model, reply.Source);
        Assert.Equal("Kahvaltı 07:00-10:30 arasındadır.", reply.Message);
    }

    [Fact]
    public async Task Kisa_mesajda_model_eminse_kategori_menusu_yerine_cevap_verilir()
    {
        // Hata: "kahvaltı fiyat" eskiden model hiç çalışmadan Yeme ve İçme menüsüne gidiyordu.
        _context.Classifier.Returns("kahvaltı fiyat", Confident("BreakfastIncluded"));

        var reply = await _context.SendAsync("kahvaltı fiyat");

        Assert.Equal(ReplyTypes.Answer, reply.Type);
        Assert.Equal("BreakfastIncluded", reply.Intent);
    }

    [Fact]
    public async Task Kisa_mesajda_model_emin_degilse_kategorinin_secenekleri_sunulur()
    {
        var reply = await _context.SendAsync("kahvaltı");

        Assert.Equal(ReplyTypes.Options, reply.Type);
        Assert.Equal(ReplySources.Category, reply.Source);
        Assert.Equal(["BreakfastTime", "BreakfastIncluded"], reply.Options!.Select(x => x.Intent));
    }

    [Fact]
    public async Task Mesaj_birden_fazla_kategoriye_uyarsa_ana_menu_gosterilir()
    {
        var reply = await _context.SendAsync("ödeme ve giriş");

        Assert.Equal(ReplyTypes.Menu, reply.Type);
    }

    [Fact]
    public async Task Model_iki_aday_arasinda_kalirsa_iki_secenek_sunulur()
    {
        _context.Classifier.Returns("sabah öğünü",
            Unsure("BreakfastTime", 0.40f, "BreakfastIncluded", 0.35f, canOfferTopTwo: true));

        var reply = await _context.SendAsync("sabah öğünü");

        Assert.Equal(ReplyTypes.Options, reply.Type);
        Assert.Equal(ReplySources.TopTwo, reply.Source);
        Assert.Equal(["BreakfastTime", "BreakfastIncluded"], reply.Options!.Select(x => x.Intent));
    }

    [Fact]
    public async Task Tek_aday_one_cikiyorsa_onun_kategorisi_sunulur()
    {
        // Emin değil (IsConfident false) ama skor 0.60, ikinci aday çok zayıf.
        _context.Classifier.Returns("odayı ne zaman boşaltıyoruz",
            Unsure("CheckOutTime", 0.60f, "BreakfastTime", 0.05f));

        var reply = await _context.SendAsync("odayı ne zaman boşaltıyoruz");

        Assert.Equal(ReplyTypes.Options, reply.Type);
        Assert.Equal(ReplySources.CandidateCategory, reply.Source);
        Assert.Contains(reply.Options!, x => x.Intent == "CheckOutTime");
    }

    [Fact]
    public async Task Hicbir_adim_karar_veremezse_fallback_doner()
    {
        var reply = await _context.SendAsync("bugün hava nasıl");

        Assert.Equal(ReplyTypes.Fallback, reply.Type);
        Assert.Equal(ReplySources.Fallback, reply.Source);
        Assert.Null(reply.Intent);
        Assert.Equal(ChatbotTestContext.FallbackMessage, reply.Message);
    }

    // ---------------------------------------------------------------- konuşma geçmişi

    [Fact]
    public async Task Takip_sorusu_onceki_konuyu_kullanir()
    {
        var first = await _context.SendAsync("kahvaltı kaçta başlıyor");
        var reply = await _context.SendAsync("peki bitişi?", first.SessionId);

        Assert.Equal("BreakfastTime", reply.Intent);
        Assert.Equal(ReplySources.FollowUp, reply.Source);
    }

    [Fact]
    public async Task Takip_kelimesi_gecse_bile_model_eminse_onceki_konu_kullanilmaz()
    {
        // Hata: "kaçta" takip ifadesi olduğu için bu soru eskiden önceki konuya (kahvaltı) gidiyordu.
        _context.Classifier.Returns("saat kaçta girebilirim", Confident("CheckInTime"));

        var first = await _context.SendAsync("kahvaltı kaçta başlıyor");
        var reply = await _context.SendAsync("saat kaçta girebilirim", first.SessionId);

        Assert.Equal("CheckInTime", reply.Intent);
        Assert.Equal(ReplySources.Model, reply.Source);
    }

    [Fact]
    public async Task Farkli_oturumlarin_gecmisi_birbirini_etkilemez()
    {
        await _context.SendAsync("kahvaltı kaçta başlıyor", sessionId: "oturum-a");
        var reply = await _context.SendAsync("peki bitişi?", sessionId: "oturum-b");

        Assert.Equal(ReplyTypes.Fallback, reply.Type);
    }

    [Fact]
    public async Task Her_mesaj_kullanici_ve_asistan_kaydi_olarak_saklanir()
    {
        var reply = await _context.SendAsync("kahvaltı kaçta başlıyor");

        var messages = await _context.Db.ConversationMessages
            .Where(x => x.SessionId == reply.SessionId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(["User", "Assistant"], messages.Select(x => x.Role));
        Assert.Equal("BreakfastTime", messages[1].DetectedIntent);
    }

    // ---------------------------------------------------------------- harici veri ve bekleyen girdi

    [Fact]
    public async Task Menuden_urun_fiyati_secilince_bot_urun_adini_sorar()
    {
        var reply = await _context.SendAsync("Bir ürünün fiyatını öğrenmek istiyorum", selectedIntent: "ProductPrice");

        Assert.Equal(ReplyTypes.Question, reply.Type);
        Assert.Equal("ProductPrice", reply.Intent);
        Assert.True(_context.Provider.Requests.Single().IsMenuSelection);
    }

    [Fact]
    public async Task Bot_bilgi_istedikten_sonraki_mesaj_o_bilgi_olarak_islenir()
    {
        var question = await _context.SendAsync("Bir ürünün fiyatını öğrenmek istiyorum", selectedIntent: "ProductPrice");
        var reply = await _context.SendAsync("AstraBook Pro 14", question.SessionId);

        Assert.Equal(ReplyTypes.Answer, reply.Type);
        Assert.Equal("ProductPrice", reply.Intent);
        Assert.Equal(ReplySources.PendingInput, reply.Source);
        Assert.Equal("AstraBook Pro 14", _context.Provider.Requests.Last().Message);
        Assert.True(reply.Debug.UsedExternalData);
    }

    [Fact]
    public async Task Bilgi_beklenirken_model_baska_bir_konudan_eminse_konu_degisir()
    {
        _context.Classifier.Returns("kartla ödeme yapabilir miyim", Confident("PaymentMethods"));

        var question = await _context.SendAsync("Bir ürünün fiyatını öğrenmek istiyorum", selectedIntent: "ProductPrice");
        var reply = await _context.SendAsync("kartla ödeme yapabilir miyim", question.SessionId);

        Assert.Equal("PaymentMethods", reply.Intent);
        Assert.Equal(ReplySources.Model, reply.Source);
    }

    // ---------------------------------------------------------------- hatalar

    [Fact]
    public async Task Bilinmeyen_bot_anahtari_BotNotFoundException_firlatir()
    {
        await Assert.ThrowsAsync<BotNotFoundException>(() => _context.Service.SendAsync(new ChatRequest
        {
            BotKey = "olmayan-bot",
            Message = "merhaba"
        }));
    }
}
