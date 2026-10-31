using Ultron.Core.Chat;

namespace Ultron.Tests.Chat;

public class AnswerCleanupTests
{
    [Theory]
    [InlineData("Kitap temalı bir hediye alabilirsin.\nBu önerilerden hangisi daha uygun görünüyor?", "Kitap temalı bir hediye alabilirsin.")]
    [InlineData("Kitap temalı bir hediye alabilirsin. Hangisi uygun?", "Kitap temalı bir hediye alabilirsin.")]
    [InlineData("* **Kitap:** nadir baskı\n* **Mum:** güzel kokulu\nBu önerilerden hangisi daha uygun görünüyor?  ", "* **Kitap:** nadir baskı\n* **Mum:** güzel kokulu")]
    public void Sondaki_soru_cumlesi_atilir(string input, string expected) =>
        Assert.Equal(expected, AnswerCleanup.RemoveTrailingQuestion(input));

    [Theory]
    [InlineData("Size nasıl yardımcı olabilirim?")]                 // cevabın kendisi soru: dokunma
    [InlineData("Entropi, düzensizliğin ölçüsüdür.")]              // soru yok
    [InlineData("Peki? Evet, öyle.")]                               // soru sonda değil
    public void Dokunulmaz(string input) =>
        Assert.Equal(input, AnswerCleanup.RemoveTrailingQuestion(input));

    [Theory]
    [InlineData(@"Formülü $\eta = 1 - T_L / T_H$ şeklindedir.", "Formülü η = 1 - T_L / T_H şeklindedir.")]
    [InlineData(@"Yüksek sıcaklık ($T_H$) ve $$\Delta U = Q - W$$ bağıntısı.", "Yüksek sıcaklık (T_H) ve Δ U = Q - W bağıntısı.")]
    [InlineData(@"Verim $\frac{W}{Q_H}$ ve $\sqrt{2}$, açı 30$^\circ$.", "Verim W/Q_H ve √2, açı 30°.")]
    [InlineData("Bilet 5$ ile 10$ arası.", "Bilet 5$ ile 10$ arası.")] // para birimi: dokunma
    public void LaTeX_duz_metne_cevrilir(string input, string expected) =>
        Assert.Equal(expected, AnswerCleanup.PlainMath(input));

    [Fact]
    public void Coklu_kaynak_numaralari_ayrilir() =>
        Assert.Equal("Şöyle [1][2]. Böyle [3].", Ultron.Core.Documents.DocumentCitations.SplitLists("Şöyle [1, 2]. Böyle [3]."));

    [Theory]
    [InlineData("Var fonksiyon kapsamlıdır, let blok kapsamlıdır.\n\nİhtiyacına göre örnekler verebilirim.", "Var fonksiyon kapsamlıdır, let blok kapsamlıdır.")]
    [InlineData("Async olanı kullan, akışı bloklamaz. Bu konularda daha fazla bilgi ister misin?", "Async olanı kullan, akışı bloklamaz.")]
    [InlineData("try-catch ile yakalanır [Genel Bilgi]. Future gelecekteki değerdir.", "try-catch ile yakalanır. Future gelecekteki değerdir.")]
    [InlineData("Plan: 1) API 2) arayüz 3) sepet. Bunu Claude Code'a vereyim mi?", "Plan: 1) API 2) arayüz 3) sepet. Bunu Claude Code'a vereyim mi?")]
    [InlineData("Dinlenmek en iyisi. Bir fincan çay iyi gelebilir.", "Dinlenmek en iyisi. Bir fincan çay iyi gelebilir.")]
    [InlineData("Servisi kaydetmeyi unutma. Servis tanımını hangi katmanda (Startup/Program.cs içinde mi) yaptın?", "Servisi kaydetmeyi unutma.")]
    [InlineData("Önce Program.cs içinde bir CORS politikası tanımla ve UseCors ile etkinleştir. ASP.NET Core tarafında örnek kodu gösterebilirim.", "Önce Program.cs içinde bir CORS politikası tanımla ve UseCors ile etkinleştir.")]
    public void Tidy_sondaki_teklif_ve_soruyu_atar_claude_code_sorusu_kalir(string input, string expected) =>
        Assert.Equal(expected, AnswerCleanup.Tidy(input));
}

public class AboutUltronTests
{
    [Theory]
    [InlineData("Sen neler yapabiliyorsun, kısaca?", true)]
    [InlineData("Kimsin sen?", true)]
    [InlineData("Bu hatayla ilgili ne yapabilirim?", false)]
    [InlineData("Neler yapabiliriz bu hafta sonu?", false)]
    public void Kendini_tanitma_sorusu(string text, bool expected) => Assert.Equal(expected, AnswerStyle.IsAboutUltron(text));
}

public class WritingRequestTests
{
    [Theory]
    [InlineData("Hocama derse yetişemeyeceğimi bildiren kısa ve saygılı bir e-posta yaz.", true)]
    [InlineData("Şu cümleyi daha kibar yaz: \"Raporu yarına kadar bitir.\"", true)]
    [InlineData("Bana C# ile bir kod yaz", false)]
    [InlineData("E-posta adresimi neden doğrulamıyor?", false)]
    public void Metin_yazma_istegi(string text, bool expected) => Assert.Equal(expected, AnswerStyle.IsWritingRequest(text));
}
