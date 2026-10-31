namespace Ultron.Core.Tools;

public enum ApprovalDecision
{
    Approved,
    Rejected,

    /// <summary>Süre içinde cevap gelmedi: reddedilmiş sayılır.</summary>
    TimedOut,

    /// <summary>Onay soracak kimse yok (masaüstü kapalı, Telegram eşleşmemiş; ya da Cli/test).</summary>
    Unavailable,
}

/// <summary>Bekleyen onay isteği: masaüstünde kart, kullanıcı uzaktaysa Telegram'da düğmeler.</summary>
/// <param name="Summary">İnsan diliyle ne yapılacağı: "Komut çalıştır: dir C:\Users".</param>
public sealed record ApprovalRequest(long Id, string Tool, string Summary, DateTime ExpiresAtUtc);

/// <summary>
/// Onaylı (seviye 2) işlemler için kullanıcıya sorar. Kapıdaki görevli gibi: işi yapmadan önce "bu kişiyi içeri alayım mı?"
/// diye ev sahibine sorar; cevap gelmezse kapıyı açmaz.
/// </summary>
public interface IApprovalService
{
    Task<ApprovalDecision> RequestAsync(string tool, string summary, CancellationToken cancellationToken = default);
}

/// <summary>Onay soracak arayüz yoksa (Cli, testler): onaylı işlemler yapılmaz.</summary>
public sealed class NoApprovalService : IApprovalService
{
    public Task<ApprovalDecision> RequestAsync(string tool, string summary, CancellationToken cancellationToken = default) =>
        Task.FromResult(ApprovalDecision.Unavailable);
}

/// <summary>Onay gösterecek arayüz yok (Cli, testler): istekler "sorulamadı" olur, onaylı işlem yapılmaz.</summary>
public sealed class NoApprovalNotifier : IApprovalNotifier
{
    public Task<bool> NotifyAsync(ApprovalRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task ClosedAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
