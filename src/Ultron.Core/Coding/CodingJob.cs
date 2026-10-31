namespace Ultron.Core.Coding;

public enum CodingStatus
{
    /// <summary>Claude Code çalışıyor.</summary>
    Calisiyor,

    /// <summary>Claude Code bir karar için soru sordu; kullanıcının cevabıyla aynı oturum devam eder.</summary>
    Soruyor,

    Bitti,
    Hata,

    /// <summary>Kullanım limiti doldu: limit yenilenince "devam et" ile sürer.</summary>
    Bekliyor,

    Iptal,
}

/// <summary>Claude Code'a devredilmiş bir kodlama işi (Faz 13).</summary>
public sealed class CodingJob
{
    public long Id { get; init; }

    /// <summary>Kısa ad: "hava-durumu-sitesi".</summary>
    public required string Title { get; set; }

    public required string Folder { get; init; }

    /// <summary>Kullanıcının ilk isteği (PLAN.md'nin özü).</summary>
    public required string Request { get; init; }

    public CodingStatus Status { get; set; } = CodingStatus.Calisiyor;

    /// <summary>Claude Code oturumu: soru-cevap ve "devam et" aynı oturumla (--resume) sürer.</summary>
    public string? SessionId { get; set; }

    /// <summary>Claude Code'un son mesajı (özet ya da soru).</summary>
    public string? LastMessage { get; set; }

    /// <summary>Son ilerleme satırı ("Program.cs düzenleniyor").</summary>
    public string? Progress { get; set; }

    public double CostUsd { get; set; }

    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive => Status is CodingStatus.Calisiyor;
}

public interface ICodingJobStore
{
    Task<CodingJob> AddAsync(CodingJob job, CancellationToken cancellationToken = default);

    Task UpdateAsync(CodingJob job, CancellationToken cancellationToken = default);

    Task<CodingJob?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodingJob>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>Claude Code'a giden tek çalıştırma: proje klasörü, istem ve (devam ediyorsa) oturum.</summary>
/// <param name="McpToken">Bu çalıştırmaya özel Ultron MCP anahtarı (proje notları, izinli hafıza); yoksa MCP bağlanmaz.</param>
public sealed record CodeRunRequest(string Folder, string Prompt, string? ResumeSessionId = null, string? McpToken = null)
{
    /// <summary>Boş değilse varsayılan araç listesinin yerine geçer (gece denetimi: sadece Read/Glob/Grep).</summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>Çalışma klasörü dışında okunabilecek klasörler (--add-dir).</summary>
    public IReadOnlyList<string> ExtraFolders { get; init; } = [];

    /// <summary>Dosya düzenlemesi otomatik kabul edilmez (acceptEdits yok): izinli araçlar dışında her şey reddedilir.</summary>
    public bool ReadOnly { get; init; }
}

/// <param name="LimitReached">Kullanım limiti doldu (iş "Bekliyor" olur).</param>
public sealed record CodeRunResult(bool Ok, string Text, string? SessionId, double CostUsd, bool LimitReached = false);

/// <summary>Claude Code'un bu bilgisayardaki durumu.</summary>
/// <param name="Executable">Bulunan claude.exe (yoksa null).</param>
public sealed record CodeAgentStatus(string? Executable, bool LoggedIn, string Message);

/// <summary>Kodu yazan dış ajan (Claude Code, headless). Ultron planlar ve yönetir, kodu bu yazar.</summary>
public interface ICodeAgent
{
    Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <param name="progress">İlerleme satırları ("Program.cs düzenleniyor") geldikçe.</param>
    Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default);
}

/// <summary>İşin durumu değişti (başladı, ilerledi, soru sordu, bitti): masaüstü ve (uzaktaysa) Telegram.</summary>
public interface ICodingNotifier
{
    Task NotifyAsync(CodingJob job, bool important, CancellationToken cancellationToken = default);
}

public sealed class LogCodingNotifier : ICodingNotifier
{
    public Task NotifyAsync(CodingJob job, bool important, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
