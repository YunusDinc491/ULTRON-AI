using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Library;

namespace Ultron.Infrastructure.Library;

/// <summary>
/// 30 dakikada bir "son pazar 03:30'dan beri güncellendi mi?" diye bakar. Açılışta 10 dk bekler (açılışı yavaşlatmasın);
/// hiç indirilmediyse ilk indirme o zaman başlar.
/// </summary>
internal sealed class LibraryScheduler(LibraryService library, TimeProvider time, ILogger<LibraryScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartDelay, time, stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                if (!library.IsSyncing && await library.TryRunScheduledAsync(stoppingToken) is { } report)
                    logger.LogInformation("Haftalık kütüphane güncellemesi bitti:\n{Report}", report);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Kütüphane zamanlayıcısı hata verdi");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
