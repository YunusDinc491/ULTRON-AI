namespace Ultron.Core.Web;

/// <summary>Bilginin ne kadar süre geçerli kaldığı ("Ansiklopedi" kaydının son kullanma tarihi).</summary>
public enum WebFreshness
{
    /// <summary>Kur, fiyat, hava, maç skoru: birkaç saat.</summary>
    Saat,

    /// <summary>Haber, gündem: bir gün.</summary>
    Gun,

    /// <summary>Yavaş değişen: kampanya, etkinlik takvimi.</summary>
    Hafta,

    /// <summary>Değişmeyen bilgi: süresiz.</summary>
    Kalici,
}

/// <summary>"Ansiklopedi": web'den öğrenilmiş, süreli bilgi. Aynı soru tekrar gelince internete gidilmez.</summary>
public sealed class WebCacheEntry
{
    public long Id { get; init; }

    /// <summary>Normalleştirilmiş önbellek anahtarı ("dolar tl kuru 2026 10 26"). Eşleşme birebir yapılır.</summary>
    public required string CacheKey { get; init; }

    /// <summary>Arama motoruna gönderilen sorgu.</summary>
    public required string Query { get; init; }

    /// <summary>Kaynaklardan üretilmiş cevap.</summary>
    public required string Answer { get; init; }

    /// <summary>Kullanılan kaynak adresleri, satır satır.</summary>
    public required string SourceUrls { get; init; }

    public WebFreshness Freshness { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Null = süresiz.</summary>
    public DateTime? ExpiresAtUtc { get; init; }
}

/// <summary>İnternete giden her sorgunun kaydı (gizlilik: kullanıcı neyin dışarı çıktığını görebilsin).</summary>
public sealed class OutgoingQuery
{
    public long Id { get; init; }

    public required string Query { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public interface IWebCacheStore
{
    /// <summary>Anahtarı birebir eşleşen ve süresi dolmamış en yeni kayıt.</summary>
    Task<WebCacheEntry?> FindValidAsync(string cacheKey, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task AddAsync(WebCacheEntry entry, CancellationToken cancellationToken = default);

    Task LogOutgoingAsync(string query, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>İnternete giden son sorgular, en yeni önce.</summary>
    Task<IReadOnlyList<OutgoingQuery>> GetRecentOutgoingAsync(int limit, CancellationToken cancellationToken = default);
}
