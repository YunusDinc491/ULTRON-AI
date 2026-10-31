using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Learning;

namespace Ultron.Infrastructure.Learning;

/// <summary>
/// 30 dakikada bir "son 04:00'ten beri denetim yapıldı mı?" diye bakar (Faz 15 d). Açılışta 15 dk bekler: açılış yükü
/// ve kütüphane zamanlayıcısıyla (10 dk) çakışmasın. Bilgisayar 04:00'te kapalıysa denetim açılıştan sonra yapılır.
/// </summary>
internal sealed class LearningScheduler(LearningService learning, Ultron.Core.Studio.StudioService studio, Ultron.Core.Lessons.LessonService lessons, Ultron.Core.Growth.GrowthService growth, TimeProvider time, ILogger<LearningScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartDelay, time, stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                if (!learning.IsAuditing && await learning.TryRunScheduledAsync(stoppingToken) is { } report)
                    await RecordAsync("yazilim", report, stoppingToken);
                // İş arayüzünün bileşenleri (Faz 16): aynı saatte, yazılım cevaplarından sonra.
                if (await studio.TryReviewAsync(stoppingToken) is { } studioReport)
                    await RecordAsync("bilesen", studioReport, stoppingToken);
                // Ders arayüzünün cevapları (Faz 17): düzeltmeler ilgili dersin notlarına.
                if (await lessons.TryReviewAsync(stoppingToken) is { } lessonReport)
                    await RecordAsync("ders", lessonReport, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Gece denetimi zamanlayıcısı hata verdi");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Rapor günlüğe ve Gelişim panosunun gece raporları geçmişine (Faz 18) yazılır; "incelenecek yok" yazılmaz.</summary>
    private async Task RecordAsync(string area, string report, CancellationToken cancellationToken)
    {
        logger.LogInformation("Gece denetimi ({Area}): {Report}", area, report);
        if (!report.StartsWith("İncelenecek", StringComparison.Ordinal))
            await growth.RecordReportAsync(area, report, cancellationToken);
    }
}
