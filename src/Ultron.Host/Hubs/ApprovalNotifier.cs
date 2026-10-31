using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Tools;
using Ultron.Host.Telegram;
using Ultron.Infrastructure.Telegram;

namespace Ultron.Host.Hubs;

/// <summary>Kaç masaüstü istemcisi bağlı (onay kartını gösterebilecek biri var mı?).</summary>
public sealed class ConnectionTracker
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Connected() => Interlocked.Increment(ref _count);

    public void Disconnected() => Interlocked.Decrement(ref _count);
}

/// <summary>
/// Onay kartı: bağlı masaüstüne her zaman; kullanıcı uzaktaysa (ya da masaüstü kapalıysa) eşleşmiş Telegram'a [✅ Onayla] [✖ Reddet] düğmeleriyle.
/// </summary>
public sealed class ApprovalNotifier(
    IHubContext<ChatHub> hub,
    ConnectionTracker connections,
    TelegramApi telegram,
    TelegramState telegramState,
    PresenceTracker presence,
    ILogger<ApprovalNotifier> logger) : IApprovalNotifier
{
    public async Task<bool> NotifyAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var desktop = connections.Count > 0;
        if (desktop)
            await hub.Clients.All.SendAsync(ChatHubContract.ApprovalRequested, new ApprovalRequestDto(request.Id, request.Tool, request.Summary, request.ExpiresAtUtc), cancellationToken);

        var phone = false;
        if (telegramState.ChatId is { } chatId && (!desktop || presence.IsAway))
        {
            try
            {
                await telegram.SendApprovalAsync(chatId, $"🔐 Onay gerekiyor:\n{request.Summary}\n(90 sn içinde cevap yoksa yapılmaz)", request.Id, cancellationToken);
                phone = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TelegramException or TaskCanceledException)
            {
                logger.LogWarning("Onay Telegram'a gönderilemedi: {Message}", ex.Message);
            }
        }

        return desktop || phone;
    }

    public Task ClosedAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.ApprovalClosed, new ApprovalClosedDto(id, decision.ToString()), cancellationToken);
}
