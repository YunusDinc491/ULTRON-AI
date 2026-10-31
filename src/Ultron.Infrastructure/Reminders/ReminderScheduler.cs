using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Briefing;
using Ultron.Core.Reminders;

namespace Ultron.Infrastructure.Reminders;

/// <summary>
/// Saatçi: 2 saniyede bir zamanı gelen hatırlatma var mı diye bakar (tek bir SQLite sorgusu, maliyeti yok denecek kadar az).
/// 10 sn idi; "çok önemli" hatırlatmanın 10 sn'lik tekrarı bir turu kaçırıp 20 sn'ye çıkıyordu.
/// Açılıştaki ilk bakışta Ultron kapalıyken kaçırılanlar da yakalanır ("kaçırıldı" diye bildirilir).
/// </summary>
internal sealed class ReminderScheduler(
    ReminderService reminders,
    BriefingService briefing,
    Ultron.Core.Suggestions.SuggestionService suggestions,
    WakeForBriefingService wake,
    TimeProvider time,
    ILogger<ReminderScheduler> logger) : BackgroundService
{
    /// <summary>Brifing dakikada bir kontrol edilir (saat gelince en geç 30 sn içinde; kaçırıldıysa açılışta hemen).</summary>
    private static readonly TimeSpan BriefingCheck = TimeSpan.FromSeconds(30);
    private DateTimeOffset _lastBriefingCheck = DateTimeOffset.MinValue;

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Masaüstü bağlanabilsin diye ilk bakıştan önce kısa bekleme (kaçırılanlar bildirimde görünsün).
        await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
        // Sabah uyandırma görevi: açılışta ayarlarla eşitlenir, ayar değişince güncellenir.
        wake.Watch();
        _ = wake.SyncAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await reminders.FireDueAsync(stoppingToken);
                if (time.GetUtcNow() - _lastBriefingCheck >= BriefingCheck)
                {
                    _lastBriefingCheck = time.GetUtcNow();
                    await briefing.TryDeliverAsync(stoppingToken);
                    await suggestions.TryDeliverAsync(stoppingToken); // kendi içinde saatte bir düşünür
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Hatırlatmalar/brifing kontrol edilemedi");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

internal sealed class LogBriefingNotifier(ILogger<LogBriefingNotifier> logger) : IBriefingNotifier
{
    public Task NotifyBriefingAsync(string text, bool late, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("BRİFİNG{Late}: {Text}", late ? " (telafi)" : "", text);
        return Task.CompletedTask;
    }
}

/// <summary>Arayüz yoksa (Cli) hatırlatma sadece günlüğe yazılır.</summary>
internal sealed class LogReminderNotifier(ILogger<LogReminderNotifier> logger) : IReminderNotifier
{
    public Task NotifyAsync(ReminderDue due, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("HATIRLATMA: {Text} ({Due}){Missed}", due.Text, due.DueAtLocal, due.Missed ? " — kaçırıldı" : "");
        return Task.CompletedTask;
    }
}
