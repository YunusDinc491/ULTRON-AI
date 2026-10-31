namespace Ultron.Core.Learning;

/// <summary>Claude Code'un gece denetimindeki kararı.</summary>
public enum ReviewVerdict
{
    Dogru,
    Eksik,
    Yanlis,
}

/// <summary>Kullanıcının Öğrenme panelindeki kararı.</summary>
public enum ReviewDecision
{
    Bekliyor,
    Onaylandi,
    Reddedildi,
}

/// <summary>
/// Ultron'un bir yazılım sorusuna verdiği cevap ve gece denetiminin sonucu (Faz 15 d).
/// Akış: cevap kaydedilir → 04:00'te Claude Code denetler → eksik/yanlışsa düzeltme önerisi → kullanıcı onaylarsa
/// benzer sorularda cevaba öncelikli bağlam olarak girer; reddedilirse benzer soru için bir daha önerilmez.
/// </summary>
public sealed class AnswerReview
{
    public long Id { get; init; }

    public DateTime AskedAtUtc { get; init; } = DateTime.UtcNow;

    public required string Question { get; init; }

    public required string Answer { get; init; }

    /// <summary>Cevapta anılan kütüphane kaynakları ("[1] React · useEffect — https://…"), satır satır.</summary>
    public string Sources { get; init; } = "";

    /// <summary>Sorunun anlam vektörü: onaylı düzeltmeyi benzer soruya bağlamak ve reddedileni tekrar önermemek için.</summary>
    public float[] Embedding { get; init; } = [];

    /// <summary>Denetlendiği an; null = henüz denetlenmedi.</summary>
    public DateTime? AuditedAtUtc { get; set; }

    public ReviewVerdict? Verdict { get; set; }

    /// <summary>Doğru bilgi: Ultron'un sonraki cevaplarda kullanacağı kısa düzeltme (Türkçe, gerekirse kod).</summary>
    public string? Correction { get; set; }

    public string? Reason { get; set; }

    /// <summary>Düzeltmenin dayandığı kaynak (resmi doküman adresi ya da kütüphane sayfası).</summary>
    public string? Reference { get; set; }

    /// <summary>Yazılım bilmeyen birine düzeltmenin anlamı, tek cümle (Öğrenme panelinde kullanıcı için).</summary>
    public string? PlainSummary { get; set; }

    /// <summary>
    /// Resmi kaynak gösterildiği için kendiliğinden onaylandı (tasarım kararı, 2026-10-28: yazılım bilgisi gerektiren onayı
    /// kullanıcıya yüklememek için). Panelde "Geri al" ile reddedilebilir.
    /// </summary>
    public bool AutoApproved { get; set; }

    public ReviewDecision Decision { get; set; } = ReviewDecision.Bekliyor;

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Öğrenme panelinde gösterilecek öneri: denetlendi, eksik/yanlış bulundu, kullanıcı henüz karar vermedi.</summary>
    public bool IsPendingSuggestion => Verdict is ReviewVerdict.Eksik or ReviewVerdict.Yanlis && Correction is { Length: > 0 } && Decision == ReviewDecision.Bekliyor;
}

public interface IAnswerReviewStore
{
    Task<AnswerReview> AddAsync(AnswerReview review, CancellationToken cancellationToken = default);

    Task UpdateAsync(AnswerReview review, CancellationToken cancellationToken = default);

    Task<AnswerReview?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Henüz denetlenmemiş cevaplar, eskiden yeniye.</summary>
    Task<IReadOnlyList<AnswerReview>> GetUnauditedAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>tasarım kararını bekleyen öneriler, yeniden eskiye.</summary>
    Task<IReadOnlyList<AnswerReview>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnswerReview>> GetDecidedAsync(ReviewDecision decision, CancellationToken cancellationToken = default);
}

/// <summary>Denetim bitti: masaüstünde bildirim + Öğrenme paneli sayacı.</summary>
public interface ILearningNotifier
{
    /// <param name="learned">Kendiliğinden onaylanan (kaynaklı) düzeltme sayısı.</param>
    /// <param name="pending">Kaynak gösterilemediği için tasarım kararını bekleyen öneri sayısı.</param>
    Task NotifyAuditedAsync(int reviewed, int learned, int pending, CancellationToken cancellationToken = default);
}

public sealed class LogLearningNotifier : ILearningNotifier
{
    public Task NotifyAuditedAsync(int reviewed, int learned, int pending, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
