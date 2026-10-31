using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ultron.Core.Tools;

/// <summary>Onay isteğini kullanıcıya ulaştırır (masaüstü kartı, uzaktaysa Telegram düğmeleri).</summary>
public interface IApprovalNotifier
{
    /// <returns>İsteği gösterebilecek biri var mı (masaüstü bağlı ya da Telegram'a gönderildi)?</returns>
    Task<bool> NotifyAsync(ApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Karar verildi ya da süre doldu: açık kartlar kapansın.</summary>
    Task ClosedAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bekleyen onaylar. Araç turu <see cref="RequestAsync"/>'te bekler; kullanıcı karttan (ya da Telegram'dan) karar verince
/// <see cref="Resolve"/> bekleyen turu uyandırır. Süre dolarsa (90 sn) işlem yapılmaz.
/// </summary>
public sealed class ApprovalBroker(IApprovalNotifier notifier, TimeProvider time, ILogger<ApprovalBroker> logger) : IApprovalService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Bu sürede cevap gelmezse işlem yapılmaz (testte kısaltılır).</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    private readonly ConcurrentDictionary<long, (ApprovalRequest Request, TaskCompletionSource<ApprovalDecision> Decision)> _pending = new();
    private long _nextId;

    public IReadOnlyList<ApprovalRequest> Pending => _pending.Values.Select(p => p.Request).OrderBy(r => r.Id).ToList();

    public async Task<ApprovalDecision> RequestAsync(string tool, string summary, CancellationToken cancellationToken = default)
    {
        var request = new ApprovalRequest(Interlocked.Increment(ref _nextId), tool, summary, time.GetUtcNow().UtcDateTime + Timeout);
        var decision = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = (request, decision);
        logger.LogInformation("Onay isteniyor #{Id}: {Summary}", request.Id, summary);

        ApprovalDecision result;
        try
        {
            if (!await notifier.NotifyAsync(request, cancellationToken))
            {
                result = ApprovalDecision.Unavailable;
            }
            else
            {
                using var timeout = new CancellationTokenSource(Timeout, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                try
                {
                    result = await decision.Task.WaitAsync(linked.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = ApprovalDecision.TimedOut;
                }
            }
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }

        logger.LogInformation("Onay #{Id}: {Decision}", request.Id, result);
        try
        {
            await notifier.ClosedAsync(request.Id, result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Onay kartı kapatılamadı #{Id}", request.Id);
        }
        return result;
    }

    /// <summary>tasarım kararı. İstek yoksa (süre doldu ya da başka yerden cevaplandı) false.</summary>
    public bool Resolve(long id, bool approve) =>
        _pending.TryGetValue(id, out var pending)
        && pending.Decision.TrySetResult(approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected);
}
