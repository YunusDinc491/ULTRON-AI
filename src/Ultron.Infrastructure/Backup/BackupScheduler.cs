using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Backup;

namespace Ultron.Infrastructure.Backup;

/// <summary>
/// 5 dakikada bir "son 03:00'ten sonra yedek var mı?" diye bakar (klasör listesi, maliyetsiz). Açılışta 2 dk bekler:
/// bilgisayar 03:00'te kapalıysa yedek açılıştan kısa süre sonra alınır, açılışı yavaşlatmaz.
/// </summary>
internal sealed class BackupScheduler(BackupService backups, TimeProvider time, ILogger<BackupScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartDelay, time, stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await backups.TryRunScheduledAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Yedek zamanlayıcısı hata verdi");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
