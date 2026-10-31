using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Text;

namespace Ultron.Core.Sites;

/// <summary>Takip edilen sitede yeni duyuru çıktı: masaüstü + Telegram (tasarım kararı, 2026-10-30).</summary>
public interface ISiteNotifier
{
    Task NotifyNewPagesAsync(KnownSite site, IReadOnlyList<SitePage> pages, CancellationToken cancellationToken = default);
}

/// <summary>Akademik takvimden okunan bir dönem: "Final Sınav Tarihleri" 26.12.2026.</summary>
public sealed record SchoolDate(string Label, DateOnly Start);

/// <summary>
/// Okul asistanı (Faz 24): gece taramasında (1) yeni duyuru sayfaları bildirilir, (2) akademik takvim PDF'indeki sınav/kayıt
/// tarihleri okunup 7, 3 ve 1 gün kala hatırlatma kurulur (aynı tarih için bir kez). Tarihler kodla okunur (model yok):
/// PDF satırı "Final Sınav Tarihleri 26 Aralık 2026 Cumartesi 04 Ocak 2027 Pazartesi" biçiminde.
/// Benzetme: okul panosuna her sabah bakıp "yeni ilan var" ve "sınava bir hafta kaldı" diyen arkadaş.
/// </summary>
public sealed partial class SchoolAssistant(ReminderService reminders, UltronSettings settings, TimeProvider time, ILogger<SchoolAssistant> logger, ISiteNotifier? notifier = null)
{
    public static readonly int[] DaysBefore = [7, 3, 1];

    private static readonly string[] NoticeWords = ["duyuru", "haber", "ilan", "etkinlik", "akademik takvim", "akademik-takvim"];

    /// <summary>Takvim satırlarında izlenenler (not giriş ve mazeret başvuru satırları değil).</summary>
    private static readonly string[] TrackedLabels = ["ara sinav tarihleri", "final sinav tarihleri", "butunleme sinav tarihleri", "ders kayit", "kayit yenileme"];

    /// <summary>Başka fakülte/programın takvimi (kullanıcı lisans öğrencisi): bunlardan hatırlatma kurulmaz.</summary>
    private static readonly string[] OtherPrograms = ["tip", "dis hekim", "dis-hekim", "ydyo", "hazirlik", "lisansustu", "lee", "yabanci diller"];

    public async Task OnNewPagesAsync(KnownSite site, IReadOnlyList<SitePage> pages, CancellationToken cancellationToken = default)
    {
        var notices = pages.Where(p => NoticeWords.Any(w => TurkishText.Normalize(Uri.UnescapeDataString(p.Url) + " " + p.Title).Contains(TurkishText.Normalize(w), StringComparison.Ordinal)))
            .Take(5).ToList();
        if (notices.Count == 0 || notifier is null)
            return;
        logger.LogInformation("{Site}: {Count} yeni duyuru bildiriliyor", site.Name, notices.Count);
        await notifier.NotifyNewPagesAsync(site, notices, cancellationToken);
    }

    /// <summary>Okunan sayfa takvim PDF'iyse sınav tarihleri için hatırlatma kurar; kurulan hatırlatma sayısı.</summary>
    public async Task<int> OnPageAsync(string url, string title, string text, CancellationToken cancellationToken = default)
    {
        var name = TurkishText.Normalize(Uri.UnescapeDataString(url) + " " + title).Replace('-', ' ');
        if (!name.Contains("akademik takvim", StringComparison.Ordinal) || OtherPrograms.Any(p => name.Contains(p.Replace('-', ' '), StringComparison.Ordinal)))
            return 0;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var created = 0;
        foreach (var date in ExtractDates(text).Where(d => d.Start > today))
            foreach (var days in DaysBefore)
            {
                var due = date.Start.AddDays(-days).ToDateTime(new TimeOnly(9, 0));
                if (due <= time.GetLocalNow().DateTime)
                    continue;
                var key = $"okul_hatirlatma_{date.Start:yyyyMMdd}_{TurkishText.Normalize(date.Label).Replace(' ', '_')}_{days}";
                if (await settings.GetInternalAsync(key, cancellationToken) is not null)
                    continue;
                var when = date.Start.ToString("d MMMM yyyy dddd", new CultureInfo("tr-TR"));
                await reminders.AddAsync($"Okul: {date.Label} {days} gün sonra başlıyor ({when}).", due, ReminderRecurrence.Yok, ReminderPriority.Onemli, cancellationToken: cancellationToken);
                await settings.SetInternalAsync(key, "1", cancellationToken);
                created++;
            }
        if (created > 0)
            logger.LogInformation("Akademik takvimden {Count} sınav hatırlatması kuruldu ({Url})", created, url);
        return created;
    }

    /// <summary>"Final Sınav Tarihleri 26 Aralık 2026 Cumartesi 04 Ocak 2027 Pazartesi" → (Final Sınav Tarihleri, 2026-12-26).</summary>
    public static IReadOnlyList<SchoolDate> ExtractDates(string text)
    {
        var result = new List<SchoolDate>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (DateLine().Match(line) is not { Success: true } m)
                continue;
            var label = m.Groups["label"].Value.Trim().TrimEnd(':', '-').Trim();
            var normalized = TurkishText.Normalize(label);
            if (!TrackedLabels.Any(t => normalized.Contains(t, StringComparison.Ordinal)) || normalized.Contains("notlari", StringComparison.Ordinal) || normalized.Contains("mazeret", StringComparison.Ordinal))
                continue;
            if (Month(m.Groups["month"].Value) is not { } month || !int.TryParse(m.Groups["day"].Value, out var day) || !int.TryParse(m.Groups["year"].Value, out var year))
                continue;
            try
            {
                var date = new DateOnly(year, month, day);
                if (!result.Any(r => r.Label == label && r.Start == date))
                    result.Add(new SchoolDate(label, date));
            }
            catch (ArgumentOutOfRangeException)
            {
                // geçersiz tarih: atla
            }
        }
        return result;
    }

    private static int? Month(string name) => TurkishText.Normalize(name) switch
    {
        "ocak" => 1, "subat" => 2, "mart" => 3, "nisan" => 4, "mayis" => 5, "haziran" => 6,
        "temmuz" => 7, "agustos" => 8, "eylul" => 9, "ekim" => 10, "kasim" => 11, "aralik" => 12,
        _ => null,
    };

    [GeneratedRegex(@"^(?<label>\D{6,120}?)\s+(?<day>\d{1,2})\s+(?<month>\p{L}+)\s+(?<year>20\d{2})\b")]
    private static partial Regex DateLine();
}
