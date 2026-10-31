using System.Globalization;
using Microsoft.Extensions.Logging;
using Ultron.Core.Settings;

namespace Ultron.Core.Backup;

/// <param name="Label">Dosya adındaki etiket: null = günlük/elle, "geri-yukleme-oncesi" = geri yüklemeden önce alınan güvenlik yedeği.</param>
public sealed record BackupFile(string Path, DateTime CreatedLocal, long Bytes, string? Label = null)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Yedeğin kendisi: veritabanı (çevrimiçi yedek) + belgeler/ayarlar/ses tek zip'te; geri yükleme.</summary>
public interface IBackupArchiver
{
    /// <summary>Asıl yedek klasörü (%USERPROFILE%\Ultron\yedekler).</summary>
    string Folder { get; }

    Task<BackupFile> CreateAsync(string folder, DateTime nowLocal, string? label, CancellationToken cancellationToken = default);

    IReadOnlyList<BackupFile> List(string folder);

    Task RestoreAsync(BackupFile backup, CancellationToken cancellationToken = default);
}

/// <summary>
/// Yedekleme (Faz 14). Kasa gibi: her gün bir kez (03:00; bilgisayar o saatte kapalıysa açılışta) her şeyin kopyası konur,
/// son 7 günün ve son 4 haftanın birer kopyası saklanır, eskiler atılır. İsteğe bağlı ikinci yer (harici disk, OneDrive klasörü):
/// takılıysa oraya da kopyalanır. Geri yüklemeden önce mevcut durum da yedeklenir (yanlış yedek seçilirse geri dönülebilsin).
/// kullanıcının kararları (2026-10-27): yerel klasör + isteğe bağlı ikinci yer, günlük, 7 günlük + 4 haftalık.
/// </summary>
public sealed class BackupService(UltronSettings settings, IBackupArchiver archiver, TimeProvider time, ILogger<BackupService> logger)
{
    public static readonly TimeOnly DailyAt = new(3, 0);
    public const int KeepDays = 7;
    public const int KeepWeeks = 4;
    public const string BeforeRestoreLabel = "geri-yukleme-oncesi";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>/durum için son denemenin hatası (başarılıysa null).</summary>
    public string? LastError { get; private set; }

    /// <summary>İkinci yere kopyalanamadıysa nedeni ("takılı değil" gibi).</summary>
    public string? SecondaryNote { get; private set; }

    /// <summary>Zamanlayıcı çağırır: son "03:00" anından sonra yedek yoksa alır.</summary>
    public async Task<BackupFile?> TryRunScheduledAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow().DateTime;
        var last = archiver.List(archiver.Folder).FirstOrDefault(b => b.Label is null);
        if (last is not null && last.CreatedLocal >= LastSlot(now))
            return null;
        return await BackupNowAsync(null, cancellationToken);
    }

    /// <summary>En son geçen 03:00 (saat 03:00'ten önceyse dünkü).</summary>
    internal static DateTime LastSlot(DateTime now)
    {
        var today = now.Date.Add(DailyAt.ToTimeSpan());
        return now >= today ? today : today.AddDays(-1);
    }

    public async Task<BackupFile?> BackupNowAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetLocalNow().DateTime;
            var file = await archiver.CreateAsync(archiver.Folder, now, label, cancellationToken);
            Prune(archiver.Folder, now);
            LastError = null;
            logger.LogInformation("Yedek alındı: {File} ({Size})", file.Path, Size(file.Bytes));
            await CopyToSecondaryAsync(file, now, cancellationToken);
            return file;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            logger.LogWarning(ex, "Yedek alınamadı");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<BackupFile> List() => archiver.List(archiver.Folder);

    /// <summary>Önce mevcut durumu yedekler, sonra seçilen yedeği geri yükler.</summary>
    public async Task<BackupFile> RestoreAsync(BackupFile backup, CancellationToken cancellationToken = default)
    {
        var safety = await BackupNowAsync(BeforeRestoreLabel, cancellationToken)
            ?? throw new InvalidOperationException("Geri yüklemeden önce mevcut durumun yedeği alınamadı, geri yükleme yapılmadı: " + LastError);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await archiver.RestoreAsync(backup, cancellationToken);
            logger.LogWarning("Yedekten geri yüklendi: {File} (öncesi: {Safety})", backup.Path, safety.Path);
            return safety;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>"en_son", "dun", "bugun", "25.10", "25.10.2026" ya da dosya adı → yedek. Geri yükleme öncesi yedekler seçilmez (adıyla istenmedikçe).</summary>
    public BackupFile? Find(string which)
    {
        var all = List();
        var text = Text.TurkishText.Normalize(which).Replace(' ', '_');
        var regular = all.Where(b => b.Label is null).ToList();
        var today = time.GetLocalNow().DateTime.Date;
        if (all.FirstOrDefault(b => string.Equals(b.Name, which.Trim(), StringComparison.OrdinalIgnoreCase)) is { } byName)
            return byName;
        if (text is "" or "en_son" or "son" or "enson" or "en_yeni")
            return regular.FirstOrDefault();
        DateTime? day = text switch
        {
            "bugun" or "bugunku" => today,
            "dun" or "dunku" => today.AddDays(-1),
            "evvelsi_gun" or "onceki_gun" => today.AddDays(-2),
            _ => null,
        };
        if (day is null)
        {
            var raw = which.Trim().Replace('/', '.');
            if (DateTime.TryParseExact(raw, ["d.M.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
                day = full;
            else if (DateTime.TryParseExact(raw + "." + today.Year, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var thisYear))
                day = thisYear;
        }
        return day is { } d ? regular.FirstOrDefault(b => b.CreatedLocal.Date == d.Date) : null;
    }

    /// <summary>/durum: "27.10 03:00 · 3,2 MB · 6 yedek · ikinci yer: E:\Yedek".</summary>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var all = List();
        var second = await settings.GetAsync(UltronSettings.SecondaryBackup, cancellationToken);
        var secondText = second == "yok" ? "ikinci yer yok" : "ikinci yer: " + second + (SecondaryNote is null ? "" : $" ({SecondaryNote})");
        if (LastError is not null)
            return $"HATA: son yedek alınamadı ({LastError}) · {secondText}";
        return all.Count == 0
            ? $"henüz yedek yok (ilk yedek bu gece 03:00'te ya da \"şimdi yedek al\" deyince) · {secondText}"
            : $"son: {all[0].CreatedLocal:dd.MM HH:mm} · {Size(all[0].Bytes)} · {all.Count} yedek · {secondText}";
    }

    private async Task CopyToSecondaryAsync(BackupFile file, DateTime now, CancellationToken cancellationToken)
    {
        SecondaryNote = null;
        var second = await settings.GetAsync(UltronSettings.SecondaryBackup, cancellationToken);
        if (second == "yok")
            return;
        try
        {
            // Harici disk takılı değilse (sürücü yoksa) sessizce atlanır; klasör yoksa oluşturulur.
            if (Path.GetPathRoot(second) is not { } root || !Directory.Exists(root))
            {
                SecondaryNote = "takılı değil, kopyalanmadı";
                return;
            }
            Directory.CreateDirectory(second);
            await using (var source = File.OpenRead(file.Path))
            await using (var target = File.Create(Path.Combine(second, file.Name)))
                await source.CopyToAsync(target, cancellationToken);
            Prune(second, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SecondaryNote = "kopyalanamadı: " + ex.Message;
            logger.LogWarning(ex, "Yedek ikinci yere kopyalanamadı: {Second}", second);
        }
    }

    private void Prune(string folder, DateTime now)
    {
        foreach (var old in ToDelete(archiver.List(folder), now))
        {
            try { File.Delete(old.Path); }
            catch (IOException ex) { logger.LogDebug(ex, "Eski yedek silinemedi: {File}", old.Path); }
        }
    }

    /// <summary>
    /// Saklanacaklar: son 7 günün her birinin en yeni yedeği, son 4 haftanın (pazartesi başlangıçlı) her birinin en yeni yedeği
    /// ve her zaman en yeni yedek. Geri yükleme öncesi yedekler ayrıca son 7 gün boyunca saklanır.
    /// </summary>
    internal static IReadOnlyList<BackupFile> ToDelete(IReadOnlyList<BackupFile> all, DateTime now)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regular = all.Where(b => b.Label is null).OrderByDescending(b => b.CreatedLocal).ToList();
        if (regular.Count > 0)
            keep.Add(regular[0].Path);
        foreach (var day in regular.Where(b => b.CreatedLocal.Date > now.Date.AddDays(-KeepDays)).GroupBy(b => b.CreatedLocal.Date))
            keep.Add(day.First().Path);
        var thisWeek = WeekStart(now);
        foreach (var week in regular.Where(b => WeekStart(b.CreatedLocal) > thisWeek.AddDays(-7 * KeepWeeks)).GroupBy(b => WeekStart(b.CreatedLocal)))
            keep.Add(week.First().Path);
        foreach (var other in all.Where(b => b.Label is not null && b.CreatedLocal.Date > now.Date.AddDays(-KeepDays)))
            keep.Add(other.Path);
        return all.Where(b => !keep.Contains(b.Path)).ToList();
    }

    private static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => (bytes / 1024d / 1024d).ToString("0.#", new CultureInfo("tr-TR")) + " MB",
    };
}
