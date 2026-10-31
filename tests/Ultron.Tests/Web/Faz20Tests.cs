using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Pro;
using Ultron.Core.Routing;
using Ultron.Core.Settings;
using Ultron.Core.Sites;
using Ultron.Core.Tools;
using Ultron.Core.Web;
using Ultron.Infrastructure.Documents;
using Ultron.Infrastructure.Web;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Web;

/// <summary>Faz 20 (derin araştırma v2): Tavily + yedek, PDF ve bağlantı okuma, link takibi, Bildiğim siteler, Medium hafızası.</summary>
public sealed class Faz20Tests : IDisposable
{
    private static readonly IPAddress PublicIp = IPAddress.Parse("93.184.216.34");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly string _keyFile = Path.Combine(Path.GetTempPath(), $"tavily-{Guid.NewGuid():N}.txt");

    public void Dispose() => File.Delete(_keyFile);

    // ---- Tavily ve yedek ----

    private const string SearxUrl = "http://127.0.0.1:8888/search?q=akademik%20takvim&format=json&language=tr-TR";
    private const string SearxBody = """{"results":[{"title":"Okul","url":"https://okul.edu.tr/takvim","content":"takvim"}]}""";

    private (FallbackWebSearch Search, StubHttpHandler Tavily, UltronSettings Settings) CreateSearch(string tavilyBody, HttpStatusCode tavilyStatus)
    {
        File.WriteAllText(_keyFile, "tvly-deneme-anahtari-123");
        var options = Options.Create(new WebOptions { TavilyKeyFile = _keyFile });
        var tavilyHandler = new StubHttpHandler().On("https://api.tavily.com/search", tavilyBody, "application/json", status: tavilyStatus);
        var searx = new StubHttpHandler().On(SearxUrl, SearxBody, "application/json");
        var settings = new UltronSettings(new InMemorySettingsStore(), new ManualTimeProvider(Now));
        var search = new FallbackWebSearch(
            new TavilyWebSearch(new HttpClient(tavilyHandler), options, NullLogger<TavilyWebSearch>.Instance),
            new SearxngWebSearch(new HttpClient(searx) { BaseAddress = new Uri("http://127.0.0.1:8888") }, options),
            settings, new ManualTimeProvider(Now), NullLogger<FallbackWebSearch>.Instance);
        return (search, tavilyHandler, settings);
    }

    [Fact]
    public async Task Tavily_sonucu_sayfa_metniyle_gelir()
    {
        var raw = new string('a', 400);
        var (search, _, _) = CreateSearch($$"""{"results":[{"title":"Takvim","url":"https://okul.edu.tr/t","content":"özet","raw_content":"{{raw}}"}]}""", HttpStatusCode.OK);

        var results = await search.SearchAsync("akademik takvim");

        Assert.Single(results);
        Assert.True(results[0].HasContent);
    }

    [Fact]
    public async Task Tavily_kotasi_dolunca_ay_sonuna_kadar_SearXNG()
    {
        var (search, tavily, settings) = CreateSearch("""{"detail":"limit"}""", (HttpStatusCode)432);

        var first = await search.SearchAsync("akademik takvim");
        var second = await search.SearchAsync("akademik takvim");

        Assert.Equal("https://okul.edu.tr/takvim", first[0].Url);
        Assert.Equal("https://okul.edu.tr/takvim", second[0].Url);
        Assert.Single(tavily.Requests); // ikinci aramada Tavily hiç denenmedi
        Assert.Equal("2026-10", await settings.GetInternalAsync(FallbackWebSearch.ExhaustedMonthKey));
    }

    [Fact]
    public async Task Anahtar_yoksa_dogrudan_SearXNG()
    {
        var (search, tavily, _) = CreateSearch("{}", HttpStatusCode.OK);
        File.Delete(_keyFile);

        var results = await search.SearchAsync("akademik takvim");

        Assert.Single(results);
        Assert.Empty(tavily.Requests);
    }

    // ---- Sayfa okuma: bağlantılar ve PDF ----

    [Fact]
    public void Baglantilar_menu_dahil_tam_adresle_toplanir()
    {
        var html = """
            <html><body><nav><a href="/ogrenci/akademik-takvim.pdf">Akademik Takvim</a><a href="/logo.png">logo</a></nav>
            <main><p>Duyurular sayfası.</p><a href="https://baska.com/x#bolum">Dış</a><a href="mailto:a@b.com">posta</a></main></body></html>
            """;

        var (text, links) = HtmlText.Parse(html, new Uri("https://okul.edu.tr/duyurular"), 1000);

        Assert.DoesNotContain("Akademik Takvim", text); // menü metne girmez
        Assert.Equal(["https://okul.edu.tr/ogrenci/akademik-takvim.pdf", "https://baska.com/x"], links.Select(l => l.Url));
        Assert.Equal("Akademik Takvim", links[0].Text);
    }

    [Fact]
    public async Task PDF_okunur()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Akademik Takvim 2026-2027: derslerin baslangici 21 Eylul", 12, new PdfPoint(25, 700), builder.AddStandard14Font(Standard14Font.Helvetica));
        var handler = new BytesHandler("https://okul.edu.tr/takvim.pdf", builder.Build(), "application/pdf");
        var reader = new HttpWebPageReader(new HttpClient(handler), Options.Create(new WebOptions()), NullLogger<HttpWebPageReader>.Instance,
            (_, _) => Task.FromResult<IPAddress[]>([PublicIp]), extractor: new DocumentTextExtractor());

        var result = await reader.ReadPageAsync("https://okul.edu.tr/takvim.pdf", 5000);

        Assert.NotNull(result);
        Assert.True(result.IsPdf);
        Assert.Contains("21 Eylul", result.Text);
    }

    private sealed class BytesHandler(string url, byte[] body, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != url)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    // ---- Medium: sayfadaki ilgili bağlantıya girme ----

    [Fact]
    public void Ilgili_baglanti_secilir_ilgisizler_atlanir()
    {
        var page = new DeepWebService.Page(new WebResult("Öğrenci İşleri", "https://oidb.okul.edu.tr/", ""), "Öğrenci işleri ana sayfa",
        [
            new WebLink("2026-2027 Akademik Takvimi", "https://oidb.okul.edu.tr/files/akademik-takvim-2026-2027.pdf"),
            new WebLink("Yemek Listesi", "https://sks.okul.edu.tr/yemek"),
            new WebLink("İletişim", "https://oidb.okul.edu.tr/iletisim"),
        ]);

        var links = DeepWebService.RelevantLinks("okulun akademik takvimi 2026 ne zaman başlıyor", [page]);

        Assert.Equal(["https://oidb.okul.edu.tr/files/akademik-takvim-2026-2027.pdf"], links.Select(l => l.Url));
    }

    [Fact]
    public void Kurumun_resmi_takvimi_tweetin_onune_gecer()
    {
        const string question = "İstanbul Teknik Üniversitesi 2026-2027 akademik takviminde güz dönemi dersleri ne zaman başlıyor";
        DeepWebService.Passage P(string url, string text) => new(new DeepWebService.Page(new WebResult("t", url, ""), text, []), 0, text);
        var official = P("https://www.sis.itu.edu.tr/TR/ogrenci/akademik-takvim/akademik-takvim.php", "Güz Yarıyılı Derslerin Başlaması 21 Eylül 2026");
        var tweet = P("https://x.com/hesap/status/1", "Güz dönemi akademik takvim açıklandı, dersler başlıyor");

        Assert.Contains("itu", DeepWebService.Entities(question));
        Assert.True(DeepWebService.Bonus(official, DeepWebService.Stems(question), DeepWebService.Entities(question))
            > DeepWebService.Bonus(tweet, DeepWebService.Stems(question), DeepWebService.Entities(question)) + 0.15);
    }

    [Fact]
    public void Paylas_linkleri_atlanir_kurumun_PDFi_secilir()
    {
        var page = new DeepWebService.Page(new WebResult("Takvim", "https://www.ornek.edu.tr/ogrenci/kayit-ve-kabul/akademik-takvim/2026-2027", ""), "Akademik takvim",
        [
            new WebLink("Paylaş", "https://www.facebook.com/sharer/sharer.php?u=https%3A%2F%2Fx.com%2Funiversiteler-ne-zaman-aciliyor-2026-2027-takviminde-guz-donemi-dersleri-final"),
            new WebLink("Tweetle", "https://x.com/intent/tweet?text=Universiteler+2026-2027+guz+donemi+dersleri+final+haftasi"),
            new WebLink("Önlisans ve Lisans", "https://www.ornek.edu.tr/docs/default-source/akademik-takvim/2026---2027/2026-2027-onlisans-lisans.pdf?sfvrsn=8b0280b5_4"),
        ]);

        var links = DeepWebService.RelevantLinks("Örnek Üniversitesi 2026-2027 güz dönemi dersleri hangi tarihte başlıyor, final haftası ne zaman?", [page]);

        Assert.Equal("https://www.ornek.edu.tr/docs/default-source/akademik-takvim/2026---2027/2026-2027-onlisans-lisans.pdf?sfvrsn=8b0280b5_4", Assert.Single(links).Url);
    }

    [Fact]
    public void En_dolu_icerik_bolumu_okunur()
    {
        var html = "<html><body><article><p>Haber kartı</p></article><main><h1>Akademik Takvim</h1>"
            + string.Concat(Enumerable.Range(1, 30).Select(i => $"<p>{i} Eylül 2026 güz yarıyılı etkinliği</p>")) + "</main></body></html>";

        var (text, _) = HtmlText.Parse(html, null, 10_000);

        Assert.Contains("30 Eylül 2026", text);
    }

    // ---- Bildiğim siteler ----

    [Fact]
    public void Robots_yasaklari_okunur()
    {
        var rules = SiteService.ParseRobots("User-agent: Googlebot\nDisallow: /g\n\nUser-agent: *\nDisallow: /admin\nDisallow: /*.php\nAllow: /\n");

        Assert.Equal(["/admin"], rules);
    }

    [Fact]
    public void Alt_alan_adi_ayni_site_sayilir()
    {
        Assert.True(SiteService.SameSite(new Uri("https://oidb.okul.edu.tr/x"), "okul.edu.tr"));
        Assert.True(SiteService.SameSite(new Uri("https://www.okul.edu.tr/"), "okul.edu.tr"));
        Assert.False(SiteService.SameSite(new Uri("https://okul.edu.tr.kotu.com/"), "okul.edu.tr"));
    }

    [Theory]
    [InlineData("akademik takvim ne zaman başlıyor", true)]
    [InlineData("okulun final haftası hangi tarihte", true)]
    [InlineData("ısı pompası nasıl çalışır", false)]
    public void Soru_siteyi_isaret_ediyor_mu(string question, bool expected)
    {
        KnownSite[] sites = [new() { Name = "Okulum (okul.edu.tr)", Url = "https://okul.edu.tr", Purpose = "akademik takvim, duyurular" }];

        Assert.Equal(expected, SiteService.Mentions(question, sites));
    }

    [Fact]
    public async Task Site_taranir_yasakli_yer_atlanir_degismeyen_tekrar_islenmez()
    {
        var pages = new Dictionary<string, WebPage>
        {
            ["https://okul.edu.tr/robots.txt"] = new("https://okul.edu.tr/robots.txt", "User-agent: *\nDisallow: /gizli", []),
            ["https://okul.edu.tr/"] = new("https://okul.edu.tr/", Long("Ana sayfa"),
                [new WebLink("Akademik Takvim", "https://oidb.okul.edu.tr/takvim.pdf"), new WebLink("Gizli", "https://okul.edu.tr/gizli/x"), new WebLink("Dış", "https://baska.com/")]),
            ["https://oidb.okul.edu.tr/takvim.pdf"] = new("https://oidb.okul.edu.tr/takvim.pdf", Long("Akademik Takvim (PDF) dersler 21 Eylül'de başlar"), [], IsPdf: true),
        };
        var reader = new DictionaryReader(pages);
        var store = new InMemorySiteStore();
        var service = new SiteService(store, reader, new FakeEmbeddingModel([]), new ManualTimeProvider(Now), NullLogger<SiteService>.Instance);
        var (site, _) = await service.AddAsync("https://okul.edu.tr", "Okulum", "akademik takvim");

        var first = await service.CrawlAsync(site!);
        var second = await service.CrawlAsync(site!);

        Assert.Contains("2 sayfa okundu (1 PDF), 2 yeni/değişen", first);
        Assert.Contains("0 yeni/değişen", second);
        Assert.DoesNotContain("https://okul.edu.tr/gizli/x", reader.Read);
        Assert.DoesNotContain("https://baska.com/", reader.Read);
        var hits = await service.SearchAsync("akademik takvim");
        Assert.Contains(hits, h => h.Page.IsPdf);
    }

    private static string Long(string text) => text + " " + string.Join(" ", Enumerable.Repeat("bilgi metni", 20));

    private sealed class DictionaryReader(Dictionary<string, WebPage> pages) : IWebPageReader
    {
        public List<string> Read { get; } = [];

        public Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(pages.GetValueOrDefault(url)?.Text);

        public Task<WebPage?> ReadPageAsync(string url, int maxChars, CancellationToken cancellationToken = default)
        {
            Read.Add(url);
            return Task.FromResult(pages.GetValueOrDefault(url));
        }
    }

    private sealed class InMemorySiteStore : ISiteStore
    {
        private readonly List<KnownSite> _sites = [];
        private readonly List<SitePage> _pages = [];
        private readonly List<SiteChunk> _chunks = [];

        public Task<IReadOnlyList<KnownSite>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KnownSite>>(_sites.ToList());

        public Task<KnownSite> AddAsync(KnownSite site, CancellationToken cancellationToken = default)
        {
            var added = new KnownSite { Id = _sites.Count + 1, Name = site.Name, Url = site.Url, Purpose = site.Purpose };
            _sites.Add(added);
            return Task.FromResult(added);
        }

        public Task UpdateAsync(KnownSite site, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_sites.RemoveAll(s => s.Id == id) > 0);

        public Task<IReadOnlyList<SitePage>> PagesAsync(long siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitePage>>(_pages.Where(p => p.SiteId == siteId).ToList());

        public Task SavePageAsync(SitePage page, IReadOnlyList<SiteChunk> chunks, CancellationToken cancellationToken = default)
        {
            _pages.RemoveAll(p => p.Url == page.Url);
            var saved = new SitePage { Id = _pages.Count + 100, SiteId = page.SiteId, Url = page.Url, Title = page.Title, ContentHash = page.ContentHash, IsPdf = page.IsPdf, UpdatedAtUtc = page.UpdatedAtUtc };
            _pages.Add(saved);
            _chunks.AddRange(chunks.Select(c => new SiteChunk { PageId = saved.Id, Ordinal = c.Ordinal, Text = c.Text, Embedding = c.Embedding }));
            return Task.CompletedTask;
        }

        public Task RemovePagesAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default)
        {
            _pages.RemoveAll(p => pageIds.Contains(p.Id));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(SiteChunk Chunk, SitePage Page)>> ChunksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(SiteChunk, SitePage)>>(_chunks.Where(c => _pages.Any(p => p.Id == c.PageId)).Select(c => (c, _pages.First(p => p.Id == c.PageId))).ToList());
    }

    [Fact]
    public void Site_araclari_adresle_ya_da_takip_sozuyle_secilir()
    {
        Assert.Contains("site_ekle", ToolRouting.Select(MessageIntent.Komut, "okulumun sitesini takip et: https://www.okul.edu.tr")!);
        Assert.Contains("siteleri_listele", ToolRouting.Select(MessageIntent.Sohbet, "hangi siteleri takip ettigin siteler bunlar mı, takip ettiğin siteleri göster")!);
        Assert.DoesNotContain("site_ekle", ToolRouting.Select(MessageIntent.Yazilim, "React ile bir web sitesi yap") ?? []);
    }

    [Theory]
    [InlineData("İTÜ 2026-2027 akademik takviminde güz dönemi dersleri ne zaman başlıyor?", false)]
    [InlineData("okulda finaller ne zaman, dersler hangi tarihte bitiyor", false)]
    [InlineData("Perşembe 10:30'da Termodinamik dersi var, programıma ekle", true)]
    [InlineData("bu dönem ders programımı göster", true)]
    public void Akademik_takvim_sorusu_ders_programi_araci_acmaz(string text, bool expected)
    {
        var tools = ToolRouting.Select(MessageIntent.Guncel, text) ?? [];

        Assert.Equal(expected, tools.Contains("ders_programi") || tools.Contains("ders_ekle"));
    }

    // ---- Medium hafızası ----

    [Fact]
    public async Task Medium_cevabi_sadece_iyiyse_hafizaya_girer()
    {
        var store = new Ultron.Tests.Chat.ChatOrchestratorTests.InMemoryProStore();
        var pro = new ProService(store, null!, new FakeEmbeddingModel([]), Options.Create(new ProOptions()), new ManualTimeProvider(Now),
            NullLogger<ProService>.Instance, new FakeChatModel("""{"kalicilik":"ay"}"""));
        WebSource[] two = [new(1, "A", "https://a.com", "x"), new(2, "B", "https://b.com", "y")];
        var good = "2026-2027 güz dönemi dersleri 21 Eylül'de başlar; vize haftası 9-13 Kasım, final haftası 4-15 Ocak. " + new string('.', 200);

        Assert.Null(await pro.LearnFromWebAsync("akademik takvim", good, [two[0]]));                                     // tek kaynak
        Assert.Null(await pro.LearnFromWebAsync("akademik takvim", "Bu bilgiyi kaynaklarda bulamadım. " + good, two)); // emin değil
        Assert.Equal("ay", await pro.LearnFromWebAsync("akademik takvim", good, two));

        var saved = Assert.Single(await store.GetActiveAsync());
        Assert.Equal("medium", saved.Origin);
        Assert.NotNull(saved.ExpiresAtUtc);
    }
}
