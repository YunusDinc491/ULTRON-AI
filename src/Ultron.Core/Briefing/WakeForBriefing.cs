using Microsoft.Extensions.Logging;
using Ultron.Core.Settings;

namespace Ultron.Core.Briefing;

/// <param name="TaskExists">Windows Görev Zamanlayıcısı'nda uyandırma görevi var mı.</param>
/// <param name="WakeTimersAllowed">Güç planında "uyandırma zamanlayıcılarına izin ver" açık mı (prizde / pilde); okunamazsa null.</param>
public sealed record WakeStatus(bool TaskExists, bool? WakeTimersAllowedOnAc, bool? WakeTimersAllowedOnBattery, string? Error = null);

/// <summary>Bilgisayarı belirli saatlerde uykudan uyandıran görev (Windows Görev Zamanlayıcısı, "bilgisayarı uyandır").</summary>
public interface IWakeScheduler
{
    Task EnsureAsync(TimeOnly at, IReadOnlyCollection<DayOfWeek> days, CancellationToken cancellationToken = default);

    Task RemoveAsync(CancellationToken cancellationToken = default);

    Task<WakeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Sabah uyandırma (Faz 8): ayar "uyandirma" açıksa brifing günlerinde brifing saatinden 2 dk önce bilgisayarı uyandıran görev kurulur;
/// brifing saati/günleri ya da ayar değişince görev güncellenir, kapatılınca silinir. tasarım kararı: varsayılan kapalı.
/// Güç planındaki "uyandırma zamanlayıcılarına izin ver" bir sistem ayarıdır: Ultron değiştirmez, sadece durumunu gösterir.
/// </summary>
public sealed class WakeForBriefingService(UltronSettings settings, IWakeScheduler wake, ILogger<WakeForBriefingService> logger)
{
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(2);

    private static readonly string[] Watched = [UltronSettings.WakeForBriefing, UltronSettings.BriefingTime, UltronSettings.BriefingDays];
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Ayar değişince görev güncellensin (Host açılışında bir kez çağrılır).</summary>
    public void Watch() => settings.Changed += key =>
    {
        if (Watched.Contains(key))
            _ = SyncAsync();
    };

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var on = await settings.GetAsync(UltronSettings.WakeForBriefing, cancellationToken) == "acik";
            var days = DaysFor(await settings.GetAsync(UltronSettings.BriefingDays, cancellationToken));
            if (on && days.Count > 0)
            {
                var at = (await settings.GetTimeAsync(UltronSettings.BriefingTime, cancellationToken)).Add(-Lead);
                await wake.EnsureAsync(at, days, cancellationToken);
                logger.LogInformation("Sabah uyandırma görevi: {At} ({Days})", at, string.Join(",", days));
            }
            else
            {
                await wake.RemoveAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sabah uyandırma görevi güncellenemedi");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>/durum için: "açık · hafta içi 07:28 · uyandırma zamanlayıcıları: izinli" gibi.</summary>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        if (await settings.GetAsync(UltronSettings.WakeForBriefing, cancellationToken) != "acik")
            return "kapalı (\"sabah bilgisayarı uyandır\" diyerek açabilirsin)";
        var status = await wake.GetStatusAsync(cancellationToken);
        var at = (await settings.GetTimeAsync(UltronSettings.BriefingTime, cancellationToken)).Add(-Lead);
        var timers = status.WakeTimersAllowedOnAc switch
        {
            true => "Windows uyandırma zamanlayıcıları: izinli",
            false => "DİKKAT: Windows'ta uyandırma zamanlayıcıları KAPALI (Güç Seçenekleri → Uyku → Uyandırma zamanlayıcılarına izin ver → Etkinleştir)",
            null => "uyandırma zamanlayıcısı ayarı okunamadı",
        };
        return status.TaskExists ? $"açık · {at:HH\\:mm} · {timers}" : "açık ama görev kurulamadı" + (status.Error is null ? "" : $" ({status.Error})");
    }

    internal static IReadOnlyList<DayOfWeek> DaysFor(string setting) => setting switch
    {
        "her_gun" => Enum.GetValues<DayOfWeek>(),
        "hafta_ici" => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        "hafta_sonu" => [DayOfWeek.Saturday, DayOfWeek.Sunday],
        _ => [],
    };
}
