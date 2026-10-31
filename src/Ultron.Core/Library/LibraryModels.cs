namespace Ultron.Core.Library;

/// <summary>
/// Yazılım Kütüphanesi'nin bir kaynağı (Faz 15): resmi dokümantasyonun açık deposu. Kütüphane benzetmesi: her kaynak bir raf,
/// her sayfa bir kitap, her parça bir fiş. Ultron internet olmadan da fişlere bakarak cevap verir; haftada bir raflar yenilenir.
/// </summary>
/// <param name="Id">Kalıcı anahtar ("mdn", "react").</param>
/// <param name="Topic">Konu etiketi ("web", "mobil").</param>
/// <param name="Repo">GitHub deposu ("mdn/content").</param>
/// <param name="Paths">İndirilecek doküman klasörleri (seyrek klon: deponun gerisi inmez).</param>
/// <param name="Extensions">İşlenecek dosya uzantıları (".md", ".mdx").</param>
/// <param name="PageUrl">Sayfanın web adresi şablonu; {path} = <paramref name="UrlRoot"/>'a göre dosya yolu (uzantısız). Boşsa GitHub adresi.</param>
public sealed record LibrarySource(
    string Id,
    string Name,
    string Topic,
    string Repo,
    string Branch,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Extensions,
    string License,
    string? PageUrl = null,
    string? UrlRoot = null);

/// <summary>Kütüphanedeki bir doküman sayfası (bir dosya).</summary>
public sealed class LibraryPage
{
    public long Id { get; init; }

    public required string SourceId { get; init; }

    /// <summary>Depo içindeki yol ("files/en-us/web/javascript/reference/global_objects/array/map/index.md").</summary>
    public required string Path { get; init; }

    public required string Title { get; set; }

    public required string Url { get; set; }

    /// <summary>Kaynağın commit'i (hangi sürümden okundu).</summary>
    public required string Version { get; set; }

    /// <summary>Temizlenmiş metnin SHA-256'sı: değişmediyse yeniden işlenmez.</summary>
    public required string ContentHash { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Kaynakta artık yok (silindi/taşındı): aramada geriye düşer, bir sonraki güncellemede de yoksa kalır.</summary>
    public bool IsStale { get; set; }
}

/// <summary>Sayfanın aranabilir parçası ("fiş").</summary>
public sealed class LibraryChunk
{
    public long Id { get; init; }

    public long PageId { get; init; }

    public int Ordinal { get; init; }

    public required string Text { get; init; }

    public float[] Embedding { get; set; } = [];
}

/// <summary>Arama sonucu: parça + sayfa bilgisi (kaynak kartı için).</summary>
public sealed record LibraryHit(LibraryChunk Chunk, LibraryPage Page, string SourceName, double Score);

/// <summary>/durum ve "kütüphane ne durumda" için özet.</summary>
public sealed record LibraryStats(int Sources, int Pages, int Chunks, DateTime? LastSyncUtc);

public interface ILibraryStore
{
    /// <summary>Bir kaynağın sayfaları (yol → sayfa): değişeni bulmak için.</summary>
    Task<IReadOnlyDictionary<string, LibraryPage>> GetPagesAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Sayfayı (yeni ya da değişmiş) parçalarıyla birlikte yazar; eski parçaları siler. Tek işlem.</summary>
    Task<LibraryPage> SavePageAsync(LibraryPage page, IReadOnlyList<LibraryChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>Sadece sürüm/tarih günceller (içerik aynı, commit değişti).</summary>
    Task TouchPagesAsync(IReadOnlyCollection<long> pageIds, string version, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task MarkStaleAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default);

    /// <summary>Anlam araması için bütün parçaların vektörleri (bir kez okunup bellekte tutulur).</summary>
    Task<IReadOnlyList<(long ChunkId, float[] Embedding)>> GetEmbeddingsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(LibraryChunk Chunk, LibraryPage Page)>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>FTS5 öneki araması; en alakalı önce.</summary>
    Task<IReadOnlyList<long>> SearchKeywordsAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default);

    Task<LibraryStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Başlığı bu API adıyla başlayan sayfaların ilk parçaları ("Array.prototype.map" → "Array.prototype.map()" sayfası).</summary>
    Task<IReadOnlyList<long>> SearchTitlesAsync(string name, int limit, CancellationToken cancellationToken = default);
}

/// <summary>Kaynağı diske indirir ya da günceller (git, seyrek klon).</summary>
public interface ILibraryFetcher
{
    /// <returns>Yerel klasör ve indirilen commit.</returns>
    Task<(string Folder, string Commit)> FetchAsync(LibrarySource source, CancellationToken cancellationToken = default);
}
