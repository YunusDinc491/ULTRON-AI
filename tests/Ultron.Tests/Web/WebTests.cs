using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Infrastructure.Web;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Web;

public class SearxngWebSearchTests
{
    [Fact]
    public async Task Sonuclar_okunur_tekrarlar_atilir_sinir_uygulanir()
    {
        var handler = new StubHttpHandler().On(
            "http://127.0.0.1:8888/search?q=dolar%20kuru&format=json&language=tr-TR",
            """
            {"results":[
              {"title":"Dolar Kuru","url":"https://a.com/dolar","content":"Canlı dolar"},
              {"title":"Dolar Kuru (aynı)","url":"https://a.com/dolar","content":"tekrar"},
              {"title":"","url":"https://bos.com","content":"başlıksız"},
              {"title":"Döviz","url":"https://b.com","content":null},
              {"title":"Üçüncü","url":"https://c.com","content":"x"}]}
            """,
            mediaType: "application/json");
        var search = new SearxngWebSearch(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8888") },
            Options.Create(new WebOptions { MaxResults = 2 }));

        var results = await search.SearchAsync("dolar kuru");

        Assert.Equal(["https://a.com/dolar", "https://b.com"], results.Select(r => r.Url));
        Assert.Equal("", results[1].Snippet);
    }
}

public class HtmlTextTests
{
    [Fact]
    public void Gurultu_atilir_ana_icerik_ve_baslik_kalir()
    {
        const string html = """
            <html><head><title>Dolar Kuru</title><style>.x{}</style></head>
            <body>
              <nav>Anasayfa Menü</nav>
              <script>alert('x')</script>
              <article><h1>Dolar bugün</h1><p>1 USD   =
                 <b>41,25</b> TL</p></article>
              <footer>© Telif</footer>
            </body></html>
            """;

        var text = HtmlText.Extract(html, maxChars: 1000);

        Assert.Equal("Dolar Kuru\nDolar bugün 1 USD = 41,25 TL", text);
    }

    [Fact]
    public void Uzun_metin_kisaltilir() =>
        Assert.Equal("aaaaa…", HtmlText.Extract($"<body>{new string('a', 50)}</body>", maxChars: 5));
}

public class HttpWebPageReaderTests
{
    private static readonly IPAddress PublicIp = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task Html_sayfa_duz_metne_cevrilir()
    {
        var handler = new StubHttpHandler().On("https://site.com/", "<html><body><main><p>Merhaba dünya</p></main></body></html>");

        var text = await CreateReader(handler).ReadTextAsync("https://site.com/");

        Assert.Equal("Merhaba dünya", text);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/api/tags")] // Ollama
    [InlineData("http://localhost:8888/")]          // SearXNG
    [InlineData("http://192.168.1.1/")]             // modem
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task Yerel_ve_ozel_adresler_okunmaz(string url)
    {
        var handler = new StubHttpHandler();
        var reader = CreateReader(handler, resolve: host => host == "localhost" ? [IPAddress.Loopback] : [PublicIp]);

        Assert.Null(await reader.ReadTextAsync(url));
        Assert.Empty(handler.Requests); // hiç istek atılmadı
    }

    [Fact]
    public async Task Yerel_adrese_yonlendirme_izlenmez()
    {
        var handler = new StubHttpHandler().Redirect("https://kotu.com/", "http://127.0.0.1:11434/api/tags");

        Assert.Null(await CreateReader(handler).ReadTextAsync("https://kotu.com/"));
        Assert.Equal(["https://kotu.com/"], handler.Requests.Select(u => u.AbsoluteUri));
    }

    [Fact]
    public async Task Desteklenmeyen_icerik_okunmaz()
    {
        // Faz 20'den beri PDF okunuyor (bkz. Faz20Tests); arşiv/ikili dosya okunmaz.
        var handler = new StubHttpHandler().On("https://site.com/dosya.zip", "PK", mediaType: "application/zip");

        Assert.Null(await CreateReader(handler).ReadTextAsync("https://site.com/dosya.zip"));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("100.100.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void Adres_siniflandirmasi(string ip, bool isPublic) =>
        Assert.Equal(isPublic, HttpWebPageReader.IsPublic(IPAddress.Parse(ip)));

    private static HttpWebPageReader CreateReader(StubHttpHandler handler, Func<string, IPAddress[]>? resolve = null) =>
        new(new HttpClient(handler),
            Options.Create(new WebOptions()),
            NullLogger<HttpWebPageReader>.Instance,
            (host, _) => Task.FromResult(resolve?.Invoke(host) ?? [PublicIp]));
}
