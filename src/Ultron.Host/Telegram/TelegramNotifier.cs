using Ultron.Core.Briefing;
using Microsoft.AspNetCore.SignalR;
using Ultron.Core.Reminders;
using Ultron.Core.Suggestions;
using Ultron.Host.Hubs;
using Ultron.Infrastructure.Telegram;

namespace Ultron.Host.Telegram;

/// <summary>
/// kullanıcı bilgisayar başında mı? Masaüstü 30 sn'de bir Windows'tan "klavye/fare kaç saniyedir kullanılmadı" bilgisini gönderir.
/// Bildirim gelmiyorsa (masaüstü kapalı) ya da 5 dk'dan uzun süredir dokunulmadıysa "uzakta" sayılır.
/// </summary>
public sealed class PresenceTracker(TimeProvider time)
{
    public static readonly TimeSpan AwayAfterIdle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(90);

    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
    private TimeSpan _idle;

    public void Report(TimeSpan idle)
    {
        _idle = idle;
        _lastReport = time.GetUtcNow();
    }

    public bool IsAway => time.GetUtcNow() - _lastReport > ReportTimeout || _idle >= AwayAfterIdle;
}

/// <summary>Hatırlatma ve brifing: masaüstüne her zaman, Telegram'a kullanıcı uzaktaysa (ve bot eşleşmişse).</summary>
public sealed class FanOutNotifier(
    HubReminderNotifier desktop,
    TelegramApi telegram,
    TelegramState state,
    PresenceTracker presence,
    IHubContext<ChatHub> hub,
    ILogger<FanOutNotifier> logger) : IReminderNotifier, IBriefingNotifier, ISuggestionNotifier, Ultron.Core.Sites.ISiteNotifier
{
    /// <summary>Öneri: masaüstüne her zaman (bildirim + kart + ses), kullanıcı uzaktaysa Telegram'a da.</summary>
    /// <summary>Okul sitesinde yeni duyuru (Faz 24, tasarım kararı): masaüstüne VE Telegram'a, uzakta olup olmadığına bakılmadan.</summary>
    public async Task NotifyNewPagesAsync(Ultron.Core.Sites.KnownSite site, IReadOnlyList<Ultron.Core.Sites.SitePage> pages, CancellationToken cancellationToken = default)
    {
        var lines = string.Join("\n", pages.Select(p => $"• {p.Title}\n  {p.Url}"));
        var text = $"📢 {site.Name}: {(pages.Count == 1 ? "yeni duyuru" : $"{pages.Count} yeni duyuru")}\n{lines}";
        await hub.Clients.All.SendAsync(Ultron.Contracts.ChatHubContract.Suggestion, text, cancellationToken);
        if (state.ChatId is { } chat)
            await SafeAsync(() => telegram.SendMessageAsync(chat, text, cancellationToken: cancellationToken));
    }

    public async Task NotifySuggestionAsync(string text, CancellationToken cancellationToken = default)
    {
        await hub.Clients.All.SendAsync(Ultron.Contracts.ChatHubContract.Suggestion, text, cancellationToken);
        if (ShouldSendToPhone())
            await SafeAsync(() => telegram.SendMessageAsync(state.ChatId!.Value, "💡 " + text, cancellationToken: cancellationToken));
    }

    public async Task NotifyAsync(ReminderDue due, CancellationToken cancellationToken = default)
    {
        await desktop.NotifyAsync(due, cancellationToken);
        if (ShouldSendToPhone())
            await SafeAsync(() => telegram.SendMessageAsync(state.ChatId!.Value, "⏰ " + due.Spoken,
                ackReminderId: due.Priority == ReminderPriority.Onemsiz ? null : due.Id,
                silent: due.Quiet && due.Priority != ReminderPriority.CokOnemli, cancellationToken));
    }

    public async Task NotifyBriefingAsync(string text, bool late, CancellationToken cancellationToken = default)
    {
        await desktop.NotifyBriefingAsync(text, late, cancellationToken);
        if (ShouldSendToPhone())
            await SafeAsync(() => telegram.SendMessageAsync(state.ChatId!.Value, "☀️ " + text, cancellationToken: cancellationToken));
    }

    private bool ShouldSendToPhone() => state.ChatId is not null && presence.IsAway;

    private async Task SafeAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TelegramException or TaskCanceledException)
        {
            logger.LogWarning("Telegram'a bildirim gönderilemedi: {Message}", ex.Message);
        }
    }
}
