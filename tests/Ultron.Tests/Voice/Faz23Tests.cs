using Ultron.Core.Voice;
using Ultron.Infrastructure.Voice;

namespace Ultron.Tests.Voice;

/// <summary>Faz 23: hızlı komut yolu (modelsiz), bulut ses yardımcıları.</summary>
public sealed class Faz23Tests
{
    [Theory]
    [InlineData("YouTube'da Tarkan Şımarık aç", "youtube", "\"islem\":\"ac\"", "Tarkan Şımarık")]
    [InlineData("Ultron, youtube'dan Sezen Aksu Gidiyorum çal", "youtube", "\"islem\":\"ac\"", "Sezen Aksu Gidiyorum")]
    [InlineData("Duman Bu Akşam şarkısını aç", "youtube", "\"islem\":\"ac\"", "Duman Bu Akşam")]
    [InlineData("videoyu 10 dakika ileri sar", "youtube", "\"saniye\":600", "\"mod\":\"ileri\"")]
    [InlineData("30 saniye geri sar", "youtube", "\"saniye\":30", "\"mod\":\"geri\"")]
    [InlineData("videonun 12. dakikasına git", "youtube", "\"saniye\":720", "\"mod\":\"git\"")]
    [InlineData("videoyu durdur", "youtube", "\"islem\":\"durdur\"", "")]
    [InlineData("durdur", "medya", "oynat_durdur", "")]
    [InlineData("sonraki şarkı", "medya", "sonraki", "")]
    [InlineData("sesi %30 yap", "ses", "\"deger\":30", "ayarla")]
    [InlineData("sesi biraz kıs", "ses", "\"islem\":\"kis\"", "")]
    [InlineData("sesi kapat", "ses", "\"islem\":\"kapat\"", "")]
    [InlineData("Chrome'u öne getir", "pencere", "\"pencere\":\"chrome\"", "one_getir")]
    public void Sik_komutlar_modelsiz_anlasilir(string text, string tool, string expect1, string expect2)
    {
        var command = VoiceCommands.Parse(text);

        Assert.NotNull(command);
        Assert.Equal(tool, command.Tool);
        var json = System.Text.RegularExpressions.Regex.Unescape(command.Arguments.GetRawText()); // Türkçe harfler JSON'da kaçış dizisiyle yazılır
        Assert.Contains(expect1, json);
        Assert.Contains(expect2, json);
    }

    [Theory]
    [InlineData("Ses neden kısık geliyor?")]
    [InlineData("YouTube algoritması nasıl çalışır")]
    [InlineData("Yarın sabah 9'da toplantıyı hatırlat")]
    [InlineData("Termodinamik vizesi ne zaman")]
    [InlineData("10 dakika sonra beni uyar")]
    [InlineData("Bu akşam ne yemek yapsam, bir öneri ver bana lütfen biraz uzun anlat ve detaylı olsun")]
    public void Soru_ve_diger_istekler_normal_yoldan_gider(string text)
    {
        Assert.Null(VoiceCommands.Parse(text));
    }

    [Fact]
    public void Kisa_onay_tek_kelime()
    {
        Assert.Equal("Açıyorum.", VoiceCommands.Parse("YouTube'da Mabel Matiz aç")!.Confirmation);
        Assert.Equal("Sardım.", VoiceCommands.Parse("2 dakika ileri sar")!.Confirmation);
    }

    [Fact]
    public void ElevenLabs_pcm_wav_ornegine_cevrilir_ve_onbellek_anahtari_kararli()
    {
        var samples = ElevenLabsTextToSpeech.Pcm16ToFloat([0x00, 0x40, 0x00, 0xC0]); // +0,5 ve -0,5

        Assert.Equal([0.5f, -0.5f], samples);
        Assert.Equal(ElevenLabsTextToSpeech.Hash("Açıyorum."), ElevenLabsTextToSpeech.Hash(" açıyorum. "));
    }
}
