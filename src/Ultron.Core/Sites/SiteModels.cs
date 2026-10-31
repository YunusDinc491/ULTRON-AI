namespace Ultron.Core.Sites;

/// <summary>
/// Ultron'un takip ettiği site (Faz 20, "Bildiğim siteler"): okulun sitesi, haber ya da duyuru sayfaları. Her gece taranır,
/// sayfaları (PDF'ler dahil) Ultron'un kendi kopyasına girer; soru gelince internete çıkmadan oradan cevaplanır.
/// </summary>
public sealed class KnownSite
{
    public long Id { get; init; }

    /// <summary>Görünen ad: "Okulum (ytu.edu.tr)".</summary>
    public required string Name { get; set; }

    /// <summary>Başlangıç adresi; aynı alan adı (ve alt alan adları) taranır.</summary>
    public required string Url { get; set; }

    /// <summary>Ne için takip ediliyor ("akademik takvim, duyurular", "günlük burç yorumu"): taramada bu kelimelere öncelik verilir.</summary>
    public string Purpose { get; set; } = "";

    public int MaxPages { get; set; } = 120;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime? LastCrawledUtc { get; set; }

    public int PageCount { get; set; }

    /// <summary>Son taramanın özeti ya da hatası ("118 sayfa, 3 PDF, 12 değişti").</summary>
    public string? LastResult { get; set; }
}

public sealed class SitePage
{
    public long Id { get; init; }

    public long SiteId { get; init; }

    public required string Url { get; init; }

    public required string Title { get; set; }

    public required string ContentHash { get; set; }

    public bool IsPdf { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SiteChunk
{
    public long Id { get; init; }

    public long PageId { get; init; }

    public int Ordinal { get; init; }

    public required string Text { get; init; }

    public float[] Embedding { get; set; } = [];
}

/// <summary>Arama sonucu: parça, sayfası ve sitesi.</summary>
public sealed record SiteHit(SiteChunk Chunk, SitePage Page, KnownSite Site, double Score);

public interface ISiteStore
{
    Task<IReadOnlyList<KnownSite>> ListAsync(CancellationToken cancellationToken = default);

    Task<KnownSite> AddAsync(KnownSite site, CancellationToken cancellationToken = default);

    Task UpdateAsync(KnownSite site, CancellationToken cancellationToken = default);

    /// <summary>Siteyi, sayfalarını ve parçalarını siler.</summary>
    Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SitePage>> PagesAsync(long siteId, CancellationToken cancellationToken = default);

    /// <summary>Sayfayı ekler ya da (adres aynıysa) günceller; parçaları baştan yazılır.</summary>
    Task SavePageAsync(SitePage page, IReadOnlyList<SiteChunk> chunks, CancellationToken cancellationToken = default);

    Task RemovePagesAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default);

    /// <summary>Etkin sitelerin tüm parçaları (sayfasıyla): arama bellekte yapılır.</summary>
    Task<IReadOnlyList<(SiteChunk Chunk, SitePage Page)>> ChunksAsync(CancellationToken cancellationToken = default);
}
