using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Chat;
using Ultron.Core.Routing;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Routing;

public class IntentRouterTests
{
    [Theory]
    [InlineData("""{"sinif":"Guncel"}""", MessageIntent.Guncel)]
    [InlineData("""{"sinif":"kisisel"}""", MessageIntent.Kisisel)]
    [InlineData("""{"sinif":"Uydurma"}""", MessageIntent.Sohbet)]
    [InlineData("""{}""", MessageIntent.Sohbet)]
    public void Model_cevabi_ayristirilir_anlasilmazsa_Sohbet(string json, MessageIntent expected) =>
        Assert.Equal(expected, IntentRouter.Parse(json));

    [Fact]
    public async Task Onceki_asistan_mesaji_baglam_olarak_gonderilir()
    {
        var chat = new FakeChatModel("""{"sinif":"Kisisel"}""");
        var router = new IntentRouter(chat, NullLogger<IntentRouter>.Instance);

        var intent = await router.ClassifyAsync("evet", "Yarın 9'da hatırlatayım mı?");

        Assert.Equal(MessageIntent.Kisisel, intent);
        Assert.Contains("Yarın 9'da hatırlatayım mı?", chat.JsonRequests[0][1].Content);
    }

    [Theory]
    [InlineData("Yarın sabah 8'de bana ilaçlarımı hatırlat.", true)]
    [InlineData("Spotify'ı açar mısın?", true)]
    [InlineData("Visual Studio'yu aç", true)]
    [InlineData("Her pazartesi 9'da alarm kur", true)]
    [InlineData("Annemi ara", true)]
    [InlineData("Kendini iki cümleyle tanıt, sonra üç maddelik bir liste yaz.", false)]
    [InlineData("Acaba acil bir durum mu var?", false)]
    [InlineData("Bana bir şiir yaz", false)]
    [InlineData("Dolar kuru ne kadar?", false)]
    public void Islem_fiili_tanınır(string message, bool expected) =>
        Assert.Equal(expected, ActionWords.Contains(message));

    [Fact]
    public async Task Islem_fiili_olmayan_Komut_Sohbete_dusurulur()
    {
        var router = new IntentRouter(new FakeChatModel("""{"sinif":"Komut"}""", """{"sinif":"Komut"}"""), NullLogger<IntentRouter>.Instance);

        Assert.Equal(MessageIntent.Sohbet, await router.ClassifyAsync("Kendini tanıt ve bir liste yaz"));
        Assert.Equal(MessageIntent.Komut, await router.ClassifyAsync("Spotify'ı aç"));
    }

    [Fact]
    public async Task Model_hata_verirse_Sohbet_varsayilir()
    {
        var router = new IntentRouter(new FakeChatModel(/* cevap yok → Dequeue hata verir */), NullLogger<IntentRouter>.Instance);

        Assert.Equal(MessageIntent.Sohbet, await router.ClassifyAsync("Merhaba"));
    }

    [Fact]
    public void Guncel_soruda_web_yoksa_uydurma_uyarisi_eklenir()
    {
        Assert.Equal([IntentNotes.NoWebAccess], IntentNotes.For(MessageIntent.Guncel, webAvailable: false));
        Assert.Empty(IntentNotes.For(MessageIntent.Guncel, webAvailable: true));
        Assert.Empty(IntentNotes.For(MessageIntent.GenelBilgi, webAvailable: false));

        var prompt = SystemPrompt.Build(DateTime.Now, notes: IntentNotes.For(MessageIntent.Guncel, false));
        Assert.EndsWith(IntentNotes.NoWebAccess, prompt);
    }

    [Fact]
    public void Komutta_arac_yoksa_model_cagrilmadan_sabit_durust_cevap_verilir()
    {
        Assert.Equal(IntentNotes.NoToolsAnswer, IntentNotes.DirectAnswer(MessageIntent.Komut, toolsAvailable: false));
        Assert.Null(IntentNotes.DirectAnswer(MessageIntent.Komut, toolsAvailable: true));
        Assert.Null(IntentNotes.DirectAnswer(MessageIntent.Sohbet, toolsAvailable: false));
    }
}
