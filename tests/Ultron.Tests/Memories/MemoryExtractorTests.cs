using Ultron.Core.Memories;

namespace Ultron.Tests.Memories;

public class MemoryExtractorTests
{
    private static readonly List<MemoryCategory> Categories =
    [
        .. MemoryCategories.BuiltIn,
        new() { Key = "Aile", Name = "Aile", Description = "Anne, baba, kardeşler", Drawer = MemoryDrawers.Patron },
        new() { Key = "Dersler", Name = "Dersler", Description = "Aldığı dersler", Drawer = MemoryDrawers.Okul },
    ];

    [Fact]
    public void Mevcut_klasor_secilince_cekmecesi_klasorden_gelir()
    {
        var result = MemoryExtractor.Parse("""
            {"bilgiler":[
              {"cekmece":"Is","kategori":"Aile","bilgi":"Kız kardeşi Elif tıp okuyor"},
              {"cekmece":"Okul","kategori":"dersler","bilgi":"Termodinamik II alıyor"}]}
            """, Categories);

        Assert.Equal(
            [new ExtractedFact("Aile", "Kız kardeşi Elif tıp okuyor", MemoryDrawers.Patron), new ExtractedFact("Dersler", "Termodinamik II alıyor", MemoryDrawers.Okul)],
            result.Facts);
    }

    [Fact]
    public void YENI_klasor_istegi_cekmece_ve_adla_okunur()
    {
        var result = MemoryExtractor.Parse("""
            {"bilgiler":[{"cekmece":"Is","kategori":"YENI","yeni_ad":"Frontend","yeni_aciklama":"React, arayüz","bilgi":"Next.js öğreniyor"}]}
            """, Categories);

        Assert.Equal([new ExtractedFact("", "Next.js öğreniyor", MemoryDrawers.Is, new ProposedCategory("Frontend", "React, arayüz"))], result.Facts);
    }

    [Fact]
    public void Adsiz_YENI_bos_metin_atlanir_gecersiz_cekmece_PATRON_olur()
    {
        var result = MemoryExtractor.Parse("""
            {"bilgiler":[
              {"cekmece":"Okul","kategori":"YENI","bilgi":"adı yok"},
              {"cekmece":"Okul","kategori":"Dersler","bilgi":"  "},
              {"cekmece":"Uydurma","kategori":"YENI","yeni_ad":"Araç","bilgi":"Arabası Egea"}]}
            """, Categories);

        var fact = Assert.Single(result.Facts);
        Assert.Equal(MemoryDrawers.Patron, fact.Drawer);
        Assert.Equal("Araç", fact.New!.Name);
    }

    [Fact]
    public void Ogrenilen_bilgi_BILGI_cekmecesine_gider_kaynak_numarasi_silinir()
    {
        var facts = MemoryExtractor.ParseLearned("""
            {"bilgiler":[{"bilgi":"Entropi düzensizliğin ölçüsüdür [1].","kategori":"YENI","yeni_ad":"Termodinamik","yeni_aciklama":"Isı ve enerji"},{"bilgi":"kısa","kategori":"YENI","yeni_ad":"X"}]}
            """, Categories);

        var fact = Assert.Single(facts);
        Assert.Equal("Entropi düzensizliğin ölçüsüdür.", fact.Content);
        Assert.Equal(MemoryDrawers.Bilgi, fact.Drawer);
        Assert.Equal("Termodinamik", fact.New!.Name);
    }

    [Fact]
    public void Soru_anlatan_kayit_ve_ogretilmeyen_genel_bilgi_atilir()
    {
        ExtractedFact[] facts =
        [
            new("Proje", "ASP.NET Core minimal API'de doğrulama hakkında bilgi edinmek istiyor.", MemoryDrawers.Is),
            new("Proje", "E-ticaret uygulaması yazılması talep edildi.", MemoryDrawers.Is),
            new("Programlama", "Index eklemek yazmayı yavaşlatır.", MemoryDrawers.Bilgi),
            new("Teknolojiler", "C# biliyor.", MemoryDrawers.Is),
        ];

        Assert.Equal(["C# biliyor."], MemoryExtractor.KeepFacts("Index neden yazmayı yavaşlatır? Ben C# biliyorum.", facts).Select(f => f.Content));
        Assert.Contains(MemoryExtractor.KeepFacts("Şunu bil, unutma: index eklemek yazmayı yavaşlatır.", facts), f => f.Drawer == MemoryDrawers.Bilgi);
    }

    [Fact]
    public void Bos_liste_bos_doner() => Assert.Empty(MemoryExtractor.Parse("""{"bilgiler":[]}""", Categories).Facts);

    [Fact]
    public void Prompt_cekmeceleri_ve_klasorleri_sema_anahtarlari_icerir()
    {
        var prompt = MemoryPrompts.Extraction(DateTime.Now, Categories);
        Assert.Contains("İŞ (cekmece=Is)", prompt);
        Assert.Contains("  - Dersler (Dersler): Aldığı dersler", prompt);
        Assert.Contains("(henüz klasör yok)", prompt); // İŞ ve BİLGİ boş
        var schema = MemoryPrompts.ExtractionSchema(Categories);
        Assert.Contains("\"Aile\"", schema);
        Assert.Contains("\"YENI\"", schema);
        Assert.Contains("\"Bilgi\"", schema);
    }

    [Theory]
    [InlineData("aile", "ailesi", true)]
    [InlineData("spor", "spor", true)]
    [InlineData("ev", "evcil hayvanlar", false)] // kısa adda önek eşleşmesi yok
    [InlineData("frontend", "backend", false)]
    public void Ayni_ad_ya_da_onek(string a, string b, bool same) => Assert.Equal(same, MemoryService.SameName(a, b));
}
