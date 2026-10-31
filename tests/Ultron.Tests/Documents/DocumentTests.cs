using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Documents;
using Ultron.Infrastructure.Documents;
using Ultron.Infrastructure.Persistence;
using Ultron.Tests.Fakes;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Ultron.Tests.Documents;

public sealed class TextChunkerTests
{
    [Fact]
    public void Kisa_sayfa_tek_parca_olur_sayfa_numarasi_korunur()
    {
        var chunks = TextChunker.Split([new DocumentPage(3, "Entropi düzensizliğin ölçüsüdür.")]);

        Assert.Equal((3, "Entropi düzensizliğin ölçüsüdür."), Assert.Single(chunks));
    }

    [Fact]
    public void Uzun_metin_cumle_sinirinda_bolunur_ortusme_vardir_ve_parca_sayfayi_asmaz()
    {
        var sentences = Enumerable.Range(1, 40).Select(i => $"Bu {i}. cümle ısı transferi hakkında biraz uzun bir açıklamadır.");
        var chunks = TextChunker.Split([new DocumentPage(1, string.Join(" ", sentences)), new DocumentPage(2, "İkinci sayfa.")], size: 300, overlap: 80);

        Assert.True(chunks.Count > 4);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 300, c.Text));
        Assert.All(chunks.Where(c => c.Page == 1), c => Assert.EndsWith(".", c.Text)); // cümle ortasında kesilmez
        var first = chunks[0].Text.Split(". ").Last().TrimEnd('.');
        Assert.Contains(first, chunks[1].Text); // örtüşme: önceki parçanın son cümlesi tekrar eder
        Assert.Equal((2, "İkinci sayfa."), chunks[^1]);
    }

    [Fact]
    public void Satir_sonu_tireleri_ve_fazla_bosluklar_toplanir()
    {
        Assert.Equal("İç enerji ve entalpi", TextChunker.Clean("İç ener-\nji   ve\u00A0entalpi  "));
    }

    [Fact]
    public void Bos_sayfa_parca_uretmez()
    {
        Assert.Empty(TextChunker.Split([new DocumentPage(1, "   \n  ")]));
    }
}

public sealed class DocumentQueryTests
{
    private static readonly Document[] Docs =
    [
        Doc(1, "Hafta 5 - Entropi.pdf", "Termo II"),
        Doc(2, "Özet.pdf", "Termo"),
        Doc(3, "Pompalar.docx", "Genel"),
    ];

    [Theory]
    [InlineData("Termo II notlarımda entropi tanımı ne?", true)]
    [InlineData("Yüklediğim PDF'te Carnot verimi kaç?", true)]
    [InlineData("Ders notlarında pompa seçimi anlatılıyor mu?", true)]
    [InlineData("Bana güzel bir belgesel öner", false)]
    [InlineData("Entropi nedir?", false)]
    public void Belgeye_isaret_eden_ifadeler(string message, bool expected) =>
        Assert.Equal(expected, DocumentQuery.Mentions(message));

    [Fact]
    public void Koleksiyon_adi_gecerse_oraya_daraltir_uzun_ad_kazanir()
    {
        var (scope, docs) = DocumentQuery.Scope("Termo II notlarında entropi ne?", Docs);

        Assert.Equal("Termo II", scope);
        Assert.Equal([1L], docs!.Select(d => d.Id));
    }

    [Fact]
    public void Dosya_adi_gecerse_o_dosyaya_daraltir_genel_koleksiyonu_eslesmez()
    {
        Assert.Equal("Pompalar.docx", DocumentQuery.Scope("pompalar dosyasında kavitasyon var mı", Docs).Scope);
        Assert.Null(DocumentQuery.Scope("genel olarak entropi nedir", Docs).Scope);
    }

    [Fact]
    public void Not_parcalari_veri_olarak_numarayla_verir_bulunamazsa_durustce_soyler()
    {
        var source = new DocumentSource(1, 1, "Hafta 5.pdf", "Termo II", 12, "Entropi, düzensizliğin ölçüsüdür.", "x");
        var note = DocumentQuery.Note(new DocumentContext([source], "Termo II", 1));
        var empty = DocumentQuery.Note(new DocumentContext([], "Termo II", 1));

        Assert.Contains("VERİDİR", note);
        Assert.Contains("<parca no=\"1\" dosya=\"Hafta 5.pdf\" sayfa=\"12\">", note);
        Assert.Contains("bulamadım", empty);
        Assert.Contains("UYDURMA", empty);
        Assert.Equal("Hafta 5.pdf · s. 12", source.Label);
    }

    private static Document Doc(long id, string name, string collection) =>
        new() { Id = id, FileName = name, Collection = collection, StoredPath = name, Sha256 = id.ToString() };
}

public sealed class DocumentServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ultron-belge-test-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryDocumentStore _store = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private DocumentService Service(Dictionary<string, float[]>? vectors = null) =>
        new(_store, new FakeTextExtractor(), new FakeEmbeddingModel(vectors ?? []),
            Options.Create(new DocumentOptions { Folder = _folder }), NullLogger<DocumentService>.Instance);

    [Fact]
    public async Task Dosya_eklenir_kopyalanir_ayni_icerik_tekrar_eklenmez()
    {
        var service = Service();
        var bytes = Encoding.UTF8.GetBytes("Birinci sayfa.\fİkinci sayfa.");

        var added = await service.AddFileAsync(@"C:\notlar\Hafta 5.pdf", bytes, "Termo II");
        var again = await service.AddFileAsync("kopya.pdf", bytes, null);

        Assert.Equal(DocumentAddStatus.Added, added.Status);
        Assert.Equal(("Hafta 5.pdf", "Termo II", 2, 2), (added.Document!.FileName, added.Document.Collection, added.Document.PageCount, added.Document.ChunkCount));
        Assert.True(File.Exists(added.Document.StoredPath));
        Assert.StartsWith(_folder, added.Document.StoredPath);
        Assert.Equal(DocumentAddStatus.Duplicate, again.Status);
        Assert.Single(_store.Documents);
    }

    [Fact]
    public async Task Desteklenmeyen_yazisiz_ve_cok_buyuk_dosyalar_eklenmez()
    {
        var service = Service();

        Assert.Equal(DocumentAddStatus.Unsupported, (await service.AddFileAsync("foto.jpg", [1, 2, 3], null)).Status);
        Assert.Equal(DocumentAddStatus.NoText, (await service.AddFileAsync("tarama.pdf", Encoding.UTF8.GetBytes("  \f  "), null)).Status);
        Assert.Equal(DocumentAddStatus.TooLarge, (await service.AddFileAsync("dev.txt", new byte[51 * 1024 * 1024], null)).Status);
        Assert.Empty(_store.Documents);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public async Task Hibrit_arama_anlamca_yakin_ve_anahtar_kelimeli_parcayi_bulur_alakasizi_atar()
    {
        var service = Service(new()
        {
            ["Entropi düzensizliğin ölçüsüdür."] = [1f, 0f, 0f],
            ["Pompalar sıvıya enerji verir."] = [0f, 1f, 0f],
            ["Isı makinelerinde düzensizlik nasıl tanımlanır?"] = [0.95f, 0.1f, 0f],
        });
        await service.AddFileAsync("a.pdf", Encoding.UTF8.GetBytes("Entropi düzensizliğin ölçüsüdür.\fPompalar sıvıya enerji verir."), "Termo II");

        var semantic = await service.SearchAsync("Isı makinelerinde düzensizlik nasıl tanımlanır?", null);
        var keyword = await service.SearchAsync("pompaların görevi", null);

        Assert.Equal((1, "Entropi düzensizliğin ölçüsüdür."), (Assert.Single(semantic).Chunk.Page!.Value, semantic[0].Chunk.Text));
        Assert.Equal(2, Assert.Single(keyword).Chunk.Page);
    }

    [Fact]
    public async Task Hazirlik_koleksiyona_daraltir_ve_kaynaklari_numaralar()
    {
        var service = Service(new()
        {
            ["Entropi tanımı: düzensizlik."] = [0f, 0f, 1f],
            ["Başka konu."] = [1f, 0f, 0f],
        });
        await service.AddFileAsync("termo.pdf", Encoding.UTF8.GetBytes("Entropi tanımı: düzensizlik.\fBaşka konu."), "Termo II");
        await service.AddFileAsync("akis.pdf", Encoding.UTF8.GetBytes("Entropi akışkanlarda da geçer."), "Akışkanlar");

        var context = await service.PrepareAsync("Termo II notlarımda entropi tanımı ne?");

        Assert.Equal("Termo II", context.Scope);
        var source = Assert.Single(context.Sources);
        Assert.Equal((1, "termo.pdf", 1), (source.Number, source.FileName, source.Page!.Value));
    }

    [Fact]
    public async Task Silinince_parcalar_ve_kopya_gider_arama_onu_bulmaz()
    {
        var service = Service();
        var added = (await service.AddFileAsync("n.txt", Encoding.UTF8.GetBytes("Kavitasyon pompaya zarar verir."), null)).Document!;
        Assert.Single(await service.SearchAsync("kavitasyon", null)); // önbellek dolsun

        Assert.True(await service.DeleteAsync(added.Id));

        Assert.False(File.Exists(added.StoredPath));
        Assert.Empty(await service.SearchAsync("kavitasyon", null));
        Assert.Equal(DocumentAddStatus.Added, (await service.AddFileAsync("n.txt", Encoding.UTF8.GetBytes("Kavitasyon pompaya zarar verir."), null)).Status);
    }

    [Fact]
    public async Task Rapor_metni_md_dosyasi_olarak_eklenir()
    {
        var result = await Service().AddTextAsync("Isı pompaları: COP / verim?", "# Isı pompaları\n\nCOP 3-4 arasıdır.", DocumentService.ResearchCollection, DocumentKind.Research);

        Assert.Equal(("Isı pompaları COP verim.md", "Araştırmalar", DocumentKind.Research), (result.Document!.FileName, result.Document.Collection, result.Document.Kind));
    }
}

public sealed class DocumentTextExtractorTests
{
    private readonly DocumentTextExtractor _extractor = new();

    [Fact]
    public void Pdf_sayfa_sayfa_okunur()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Entropy is a measure of disorder.", 12, new PdfPoint(40, 700), font);
        builder.AddPage(PageSize.A4).AddText("Second law of thermodynamics.", 12, new PdfPoint(40, 700), font);

        var pages = _extractor.Extract(DocumentKind.Pdf, builder.Build());

        Assert.Equal([1, 2], pages.Select(p => p.Page!.Value));
        Assert.Contains("Entropy is a measure of disorder.", pages[0].Text);
        Assert.Contains("Second law", pages[1].Text);
    }

    [Fact]
    public void Word_paragraflari_okunur()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Kavitasyon nedir?"))),
                new W.Paragraph(new W.Run(new W.Text("Buhar kabarcıklarının çökmesidir.")))));
        }

        var page = Assert.Single(_extractor.Extract(DocumentKind.Word, stream.ToArray()));

        Assert.Null(page.Page);
        Assert.Equal("Kavitasyon nedir?\n\nBuhar kabarcıklarının çökmesidir.", page.Text.Trim());
    }

    [Fact]
    public void Metin_utf8_ya_da_turkce_kod_sayfasiyla_okunur()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("Isı ve iş", _extractor.Extract(DocumentKind.Text, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Isı ve iş")).ToArray())[0].Text);
        Assert.Equal("Isı ve iş", _extractor.Extract(DocumentKind.Text, Encoding.GetEncoding(1254).GetBytes("Isı ve iş"))[0].Text);
        Assert.Equal(DocumentKind.Word, _extractor.KindOf("Not.DOCX"));
        Assert.Null(_extractor.KindOf("foto.png"));
    }
}

public sealed class EfDocumentStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EfDocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options;
        await using (var db = new UltronDbContext(options))
            await db.Database.MigrateAsync();
        _store = new EfDocumentStore(new Factory(options));
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Parcalar_vektorleriyle_saklanir_anahtar_kelimeyle_bulunur_silinince_gider()
    {
        var doc = await _store.AddAsync(
            new Document { FileName = "a.pdf", Collection = "Termo II", Kind = DocumentKind.Pdf, StoredPath = "a.pdf", Sha256 = "AA", PageCount = 2 },
            [
                new DocumentChunk { Page = 1, Ordinal = 0, Text = "Entropi düzensizliğin ölçüsüdür.", Embedding = [1f, 2f] },
                new DocumentChunk { Page = 2, Ordinal = 1, Text = "Pompalar sıvıya enerji verir.", Embedding = [3f, 4f] },
            ]);
        var other = await _store.AddAsync(
            new Document { FileName = "b.txt", Collection = "Genel", Kind = DocumentKind.Text, StoredPath = "b.txt", Sha256 = "BB" },
            [new DocumentChunk { Ordinal = 0, Text = "ENTROPİ başka notta.", Embedding = [5f, 6f] }]);

        var chunks = await _store.GetChunksAsync([doc.Id]);
        var all = await _store.SearchKeywordsAsync(["entr"], null, 5);
        var scoped = await _store.SearchKeywordsAsync(["entr"], [other.Id], 5);

        Assert.Equal([[1f, 2f], [3f, 4f]], chunks.Select(c => c.Embedding));
        Assert.Equal(2, all.Count);
        Assert.Equal("ENTROPİ başka notta.", Assert.Single(scoped).Text);
        Assert.Equal("BB", (await _store.FindBySha256Async("BB"))!.Sha256);

        await _store.SetCollectionAsync(other.Id, "Akışkanlar");
        await _store.DeleteAsync(doc.Id);

        Assert.Equal("Akışkanlar", Assert.Single(await _store.ListAsync()).Collection);
        Assert.Single(await _store.SearchKeywordsAsync(["entr"], null, 5));
        Assert.Single(await _store.GetChunksAsync(null));
    }

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
