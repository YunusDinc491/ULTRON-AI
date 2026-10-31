using Ultron.Core.Chat;

namespace Ultron.Tests.Chat;

public class AnswerStyleTests
{
    [Theory]
    [InlineData("Termodinamiğin 2. yasasını detaylı anlat")]
    [InlineData("Bunu ayrıntılı açıklar mısın")]
    [InlineData("Faydalarını madde madde yaz")]
    [InlineData("Hediye fikirlerini listele")]
    [InlineData("Kurulumu adım adım anlat")]
    [InlineData("UZUN UZUN anlat şunu")]
    public void Acikca_istenince_ayrintili(string text) => Assert.True(AnswerStyle.IsDetailRequested(text));

    [Theory]
    [InlineData("Entropi nedir?")]
    [InlineData("Sevgilime ne hediye alabilirim")]
    [InlineData("Periyodik tablo kim buldu")]
    [InlineData("Bugün nasılsın")]
    public void Varsayilan_kisa_sohbet(string text) => Assert.False(AnswerStyle.IsDetailRequested(text));
}

public class AnswerCountTests
{
    [Theory]
    [InlineData("Pompaların kullanım alanlarını tam 4 madde halinde listele.", 4)]
    [InlineData("Bunu 3 adımda anlat", 3)]
    [InlineData("5 öneri ver", 5)]
    [InlineData("Termodinamiği detaylı anlat", null)]
    [InlineData("2026 yılında ne oldu", null)]
    public void Madde_sayisi_taninir(string text, int? expected) => Assert.Equal(expected, AnswerStyle.RequestedCount(text));

    [Fact]
    public void Sayi_verildiyse_ayrinti_notu_yerine_sayi_notu()
    {
        var notes = AnswerStyle.NotesFor("Kompresör türlerini tam 5 madde halinde listele");

        Assert.DoesNotContain(AnswerStyle.DetailNote, notes);
        Assert.Contains("TAM 5 madde", Assert.Single(notes));
        Assert.Equal([AnswerStyle.DetailNote, AnswerStyle.EngineeringNote], AnswerStyle.NotesFor("Isı transferini detaylı anlat"));
        Assert.Equal([AnswerStyle.EngineeringNote], AnswerStyle.NotesFor("Entropi nedir?")); // ders sorusu: denklemli kısa tanım
        Assert.Empty(AnswerStyle.NotesFor("Selam, nasılsın?"));
    }

    [Theory]
    [InlineData("Carnot veriminin formülü ne?", true)]
    [InlineData("Isı iletim denklemini yaz", true)]
    [InlineData("Reynolds sayısı nasıl hesaplanır?", true)]
    [InlineData("Bir dairenin yarıçapı 2 metre. Çevresi kaç metre? Sadece sayıyı yaz.", false)]
    public void Formul_notu_sadece_formul_sorularinda(string question, bool expected) =>
        Assert.Equal(expected, AnswerStyle.NotesFor(question).Contains(AnswerStyle.MathNote));
}

public class ArithmeticNoteTests
{
    [Theory]
    [InlineData("Bir ürün önce %20 zamlanıp sonra %20 indirime girerse ilk fiyata göre ne olur?", true)] // tur 9
    [InlineData("Saatte 60 km giden bir araç 45 dakikada kaç km yol alır?", true)]
    [InlineData("Bugün ne yapsam?", false)]
    public void Hesap_sorusu_taninir(string text, bool expected) => Assert.Equal(expected, AnswerStyle.NeedsArithmetic(text));

    [Fact]
    public void Hesap_sorusunda_ilk_cumle_cevap_notu_eklenmez() =>
        Assert.DoesNotContain(AnswerStyle.ReasoningNote, AnswerStyle.NotesFor("2 kW'lık ısıtıcı 1 kg suyu 20 °C'den 100 °C'ye neden ve kaç saniyede getirir?"));
}
