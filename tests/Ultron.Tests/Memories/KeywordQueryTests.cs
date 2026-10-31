using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Tests.Memories;

public class KeywordQueryTests
{
    [Theory]
    [InlineData("Pamuk'un ŞEHRİ", "pamuk un sehri")]
    [InlineData("Işık, İğne; çöğüş!", "isik igne cogus")]
    [InlineData("  ASELSAN'a   başvurdum  ", "aselsan a basvurdum")]
    public void Turkce_metin_normallestirilir(string input, string expected) =>
        Assert.Equal(expected, TurkishText.Normalize(input));

    [Fact]
    public void Soru_kelimeleri_atilir_kokler_4_harf_onek_olur()
    {
        var prefixes = KeywordQuery.Prefixes("Kedimin adı neydi? Bana Pamuk'u hatırlıyor musun?");

        Assert.Equal(["kedi", "pamu"], prefixes);
    }

    [Fact]
    public void Ekli_kelimeler_ayni_onekte_bulusur()
    {
        Assert.Equal(KeywordQuery.Prefixes("dersleri"), KeywordQuery.Prefixes("dersini"));
        Assert.Equal(KeywordQuery.Prefixes("Ankara'da"), KeywordQuery.Prefixes("ankaraya"));
    }

    [Fact]
    public void Sadece_kisa_ve_yaygin_kelimeler_varsa_bos_doner() =>
        Assert.Empty(KeywordQuery.Prefixes("Ne var ne yok?"));
}
