using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ultron.Core.Sites;

namespace Ultron.Infrastructure.Sites;

/// <summary>
/// Bildiğim siteler (Faz 20): eklenen ya da "şimdi tara" denen site hemen; her gün 04:30'dan sonra (bilgisayar açıksa) o gün
/// taranmamış siteler. Açılışta 2 dk bekler (açılışı yavaşlatmasın). Tarama sırayla, tek tek.
/// </summary>
internal sealed class SiteScheduler(SiteService sites, TimeProvider time, ILogger<SiteScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var site in sites.DueSites(await sites.ListAsync(stoppingToken)))
                    await sites.CrawlAsync(site, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Gece site taraması hata verdi");
            }

            // 30 dakika bekle ya da bir tarama isteği gelsin.
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(TimeSpan.FromMinutes(30));
            try
            {
                while (await sites.Requests.WaitToReadAsync(wait.Token))
                    while (sites.Requests.TryRead(out var id))
                        if ((await sites.ListAsync(stoppingToken)).FirstOrDefault(s => s.Id == id) is { } site)
                        {
                            try
                            {
                                await sites.CrawlAsync(site, stoppingToken);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                logger.LogWarning(ex, "Site taranamadı: {Site}", site.Name);
                            }
                        }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // 30 dakika doldu: gece kontrolüne dön
            }
        }
    }
}
