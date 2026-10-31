using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Embeddings;
using Ultron.Core.Library;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Persistence;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Library;

public class DocCleanerTests
{
    [Fact]
    public void Mdn_sayfasi_on_bilgi_makro_link_ve_etiketlerden_temizlenir_kod_kalir()
    {
        const string raw = """
            ---
            title: Array.prototype.map()
            slug: Web/JavaScript/Reference/Global_Objects/Array/map
            ---

            {{JSRef}}

            The **`map()`** method of {{jsxref("Array")}} instances creates a new array. See [`forEach()`](/en-US/docs/Web/forEach) and {{jsxref("Array/filter", "filter()")}}.

            <!-- yorum -->
            {{EmbedInteractiveExample("pages/js/array-map.html")}}

            ## Syntax

            ```js
            map(callbackFn)
            const doubled = [1, 2].map((x) => x * 2); // <div> burada kalmalı
            ```

            Use the `<div>` element <span class="x">carefully</span>.
            """;
        var page = DocCleaner.Clean(raw, "files/en-us/web/javascript/reference/global_objects/array/map/index.md");

        Assert.Equal("Array.prototype.map()", page.Title);
        Assert.Contains("of Array instances", page.Text);
        Assert.Contains("See `forEach()` and filter().", page.Text);
        Assert.Contains("const doubled = [1, 2].map((x) => x * 2); // <div> burada kalmalı", page.Text);
        Assert.Contains("Use the `<div>` element carefully.", page.Text);
        Assert.DoesNotContain("{{", page.Text);
        Assert.DoesNotContain("yorum", page.Text);
        Assert.DoesNotContain("slug:", page.Text);
        Assert.Contains(page.Sections, s => s.Heading == "Syntax");
    }

    [Fact]
    public void Mdx_import_ve_bilesenleri_silinir_parcalar_baslik_baglami_tasir()
    {
        const string raw = """
            ---
            title: Installation
            ---
            import { Callout } from 'nextra/components'

            <Callout type="info">Requires Node.js 18.</Callout>

            ## Manual install

            Run the command below.
            """;
        var page = DocCleaner.Clean(raw, "docs/01-app/01-getting-started/01-installation.mdx");
        Assert.DoesNotContain("import", page.Text);
        Assert.Contains("Requires Node.js 18.", page.Text);
        var chunks = DocCleaner.Chunk(page);
        Assert.Contains(chunks, c => c.StartsWith("Installation › Manual install\n"));
    }

    [Fact]
    public void Asciidoc_baslik_ve_linkgit_cevrilir()
    {
        var page = DocCleaner.Clean("git-commit(1)\n=============\n:doctype: manpage\n\n== NAME\nRecord changes, see linkgit:git-add[1].\n", "Documentation/git-commit.adoc");
        Assert.Contains("see git-add.", page.Text);
        Assert.DoesNotContain(":doctype:", page.Text);
        Assert.Contains("## NAME", page.Text);
    }

    [Fact]
    public void Microsoft_Learn_sayfasinda_sadece_en_yeni_surum_kalir_docfx_isaretleri_temizlenir()
    {
        var raw = """
            ---
            title: Minimal APIs quick reference
            ms.date: 10/01/2026
            ---
            # Minimal APIs

            :::moniker range=">= aspnetcore-10.0"
            Validation is built in for .NET 10.
            :::moniker-end

            :::moniker range=">= aspnetcore-8.0 < aspnetcore-10.0"
            Use a filter for validation in older versions.
            :::moniker-end

            > [!NOTE]
            > Route groups share a prefix.

            [!INCLUDE[](~/includes/requires-net.md)]
            :::code language="csharp" source="~/samples/Program.cs" id="snippet_1":::
            Returns <xref:Microsoft.AspNetCore.Http.IResult?displayProperty=nameWithType> from [MapGet](xref:Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet*).
            """;

        var page = DocCleaner.Clean(raw, "aspnetcore/fundamentals/minimal-apis.md");

        Assert.Equal("Minimal APIs quick reference", page.Title);
        Assert.Contains("Validation is built in for .NET 10.", page.Text);
        Assert.DoesNotContain("older versions", page.Text); // eski sürüm bloğu atıldı
        Assert.DoesNotContain(":::", page.Text);
        Assert.DoesNotContain("INCLUDE", page.Text);
        Assert.Contains("> Not:", page.Text);
        Assert.Contains("Returns Microsoft.AspNetCore.Http.IResult from MapGet.", page.Text);
    }

    [Fact]
    public void Surum_araligi_tek_surum_ve_alt_sinir_dogru_okunur()
    {
        var text = ":::moniker range=\"aspnetcore-7.0\"\nyedi\n:::moniker-end\n:::moniker range=\"< aspnetcore-7.0\"\neski\n:::moniker-end\n";
        Assert.Equal("yedi", DocCleaner.LatestMoniker(text).Trim());
    }

    [Theory]
    [InlineData("dotnet", "docs/csharp/language-reference/keywords/async.md", "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/async")]
    [InlineData("aspnetcore", "aspnetcore/fundamentals/minimal-apis.md", "https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis")]
    [InlineData("efcore", "entity-framework/core/querying/index.md", "https://learn.microsoft.com/en-us/ef/core/querying")]
    [InlineData("mdn", "files/en-us/web/javascript/reference/global_objects/array/map/index.md", "https://developer.mozilla.org/en-US/docs/web/javascript/reference/global_objects/array/map")]
    [InlineData("react", "src/content/reference/react/useEffect.md", "https://react.dev/reference/react/useEffect")]
    [InlineData("nodejs", "doc/api/fs.md", "https://nodejs.org/api/fs.html")]
    [InlineData("typescript", "packages/documentation/copy/en/handbook-v2/Basics.md", "https://github.com/microsoft/TypeScript-Website/blob/v2/packages/documentation/copy/en/handbook-v2/Basics.md")]
    public void Sayfa_adresi(string sourceId, string path, string expected) =>
        Assert.Equal(expected, LibrarySources.UrlFor(LibrarySources.Default.Single(s => s.Id == sourceId), path));
}

public class LibrarySourceFileTests
{
    [Fact]
    public void Eski_dosyaya_yeni_varsayilan_kaynaklar_eklenir_kullanici_duzenlemesi_korunur()
    {
        var mdn = LibrarySources.Default.Single(s => s.Id == "mdn") with { Paths = [] }; // kullanıcı kapatmış
        var list = new List<LibrarySource> { mdn };

        var added = Ultron.Infrastructure.Library.LibrarySourceFile.MergeDefaults(list);

        Assert.Contains("dotnet", added);
        Assert.Contains("efcore", added);
        Assert.DoesNotContain("mdn", added);
        Assert.Empty(list.Single(s => s.Id == "mdn").Paths);
        Assert.Equal(LibrarySources.Default.Count, list.Count);
    }
}

public class LibrarySearchHelperTests
{
    [Theory]
    [InlineData("2026-11-01T10:00", "2026-11-01T03:30")] // pazar sabahı, saatten sonra
    [InlineData("2026-11-01T03:00", "2026-10-25T03:30")] // pazar, saatten önce → geçen pazar
    [InlineData("2026-10-28T12:00", "2026-10-25T03:30")] // çarşamba
    public void Haftalik_zaman_dilimi(string now, string expected) =>
        Assert.Equal(DateTime.Parse(expected), LibraryService.LastSlot(DateTime.Parse(now)));

    [Fact]
    public void Kelime_terimleri_kod_adlarini_butun_alir_soru_kelimelerini_atar() =>
        Assert.Equal(["useeffect", "iki", "kez"], LibraryService.Terms("useEffect neden iki kez çalışıyor?"));

    [Fact]
    public void Vektor_dizini_en_yakinlari_siralar()
    {
        float[] Unit(int axis) { var v = new float[LibraryService.IndexDimensions]; v[axis] = 1; return v; }
        var a = LibraryService.Truncate(Unit(0));
        var b = LibraryService.Truncate([0.6f, 0.8f]);
        var c = LibraryService.Truncate(Unit(1));
        var index = (new long[] { 1, 2, 3 }, a.Concat(b).Concat(c).ToArray());
        Assert.Equal([1, 2], LibraryService.TopByVector(index, Unit(0), 2));
        Assert.Equal(1f, LibraryService.Dot(a, a), 3);
    }
}

/// <summary>Gerçek SQLite (FTS5 dahil) + sahte indirici: yeni, değişmeyen, değişen ve silinen sayfa.</summary>
public sealed class LibrarySyncTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-kutuphane-test-" + Guid.NewGuid().ToString("N"));
    private DbContextOptions<UltronDbContext> _options = null!;
    private LibraryService _library = null!;
    private readonly FolderFetcher _fetcher = new();
    private static readonly LibrarySource Source = new("deneme", "Deneme Dokümanları", "web", "x/y", "main", ["docs"], [".md"], "MIT", "https://ornek.dev/{path}", "docs");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo", "docs"));
        _options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "t.db")}").Options;
        await using (var db = new UltronDbContext(_options))
            await db.Database.MigrateAsync();
        _fetcher.Folder = Path.Combine(_root, "repo");
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        _library = new LibraryService(new EfLibraryStore(new Factory(_options)), _fetcher, new FakeEmbeddingModel([]),
            new UltronSettings(new InMemorySettingsStore(), time), time, NullLogger<LibraryService>.Instance) { Sources = [Source] };
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_root, "repo", "docs", name), text);

    private static string Page(string title, string body) => $"---\ntitle: {title}\n---\n\n{body} Bu cümle sayfanın yeterince uzun olması için ek açıklama içerir ve aramada bulunur.";

    [Fact]
    public async Task Ilk_eslemede_eklenir_sonra_sadece_degisen_islenir_silinen_eskir()
    {
        Write("flexbox.md", Page("Flexbox", "Flexbox tek eksenli yerleşim sistemidir; justify-content ana eksende hizalar."));
        Write("grid.md", Page("Grid", "Grid iki eksenli yerleşim sistemidir; grid-template-columns sütunları tanımlar."));
        Write("kisa.md", "çok kısa");
        Directory.CreateDirectory(Path.Combine(_root, "repo", "docs", "includes"));
        Write(Path.Combine("includes", "eski.md"), Page("Eski", "Eski sürüm parçası justify-content.")); // DocFX gömme parçası: alınmaz
        _fetcher.Commit = "aaaaaaa1";

        var first = await _library.SyncAllAsync();
        Assert.Contains("2 sayfa (2 yeni, 0 değişti, 0 kaldırıldı)", first);
        var hit = Assert.Single(await _library.SearchAsync("justify-content", 1));
        Assert.Equal("Flexbox", hit.Page.Title);
        Assert.Equal("https://ornek.dev/flexbox", hit.Page.Url);
        Assert.Equal("Deneme Dokümanları", hit.SourceName);

        Write("grid.md", Page("Grid", "Grid iki eksenli yerleşimdir; grid-template-areas bölgeleri adlandırır."));
        File.Delete(Path.Combine(_root, "repo", "docs", "flexbox.md"));
        _fetcher.Commit = "bbbbbbb2";

        var second = await _library.SyncAllAsync();
        Assert.Contains("1 sayfa (0 yeni, 1 değişti, 1 kaldırıldı)", second);
        Assert.DoesNotContain(await _library.SearchAsync("justify-content", 3), h => h.Page.Title == "Flexbox"); // eskidi
        Assert.Equal("Grid", (await _library.SearchAsync("grid-template-areas", 1))[0].Page.Title);
        var stats = await _library.StatsAsync();
        Assert.Equal(1, stats.Pages);
        Assert.NotNull(stats.LastSyncUtc);
        Assert.Null(await _library.TryRunScheduledAsync()); // bu hafta zaten güncellendi

        Write("grid.md", Page("Grid", "Diskte değişti ama commit aynı: kaynak tamamlanmış sayıldığı için taranmaz."));
        Assert.Contains("Deneme Dokümanları: değişmedi", await _library.SyncAllAsync());
    }

    [Fact]
    public async Task Sorguda_tam_API_adi_gecerse_o_basliktaki_sayfa_one_cikar()
    {
        Write("map-foreach.md", Page("Map.prototype.forEach()", "The forEach() method of Map instances executes a function once per key/value pair, forEach forEach."));
        Write("array-map.md", Page("Array.prototype.map()", "The map() method of Array instances creates a new array populated with the results."));
        _fetcher.Commit = "c1";
        await _library.SyncAllAsync();

        var hits = await _library.SearchAsync(["map ile forEach farkı", "Array.prototype.map vs Array.prototype.forEach"], 2);

        Assert.Equal("Array.prototype.map()", hits[0].Page.Title);
    }

    private sealed class FolderFetcher : ILibraryFetcher
    {
        public string Folder { get; set; } = "";
        public string Commit { get; set; } = "";

        public Task<(string Folder, string Commit)> FetchAsync(LibrarySource source, CancellationToken cancellationToken = default) =>
            Task.FromResult((Folder, Commit));
    }

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
