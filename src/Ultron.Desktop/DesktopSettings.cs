using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Ultron.Desktop;

/// <summary>
/// Masaüstü tercihleri: %USERPROFILE%\Ultron\desktop-settings.json (AppData değil: Claude uygulamasının sanal
/// AppData'sı sorun çıkarıyordu). Ses açık/kapalı oturum boyunca bellekte; Windows açılışında başlatma kalıcı.
/// </summary>
public static class DesktopSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Ultron";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "desktop-settings.json");

    /// <summary>Sesli cevap, HUD sesleri ve sesli hatırlatma (üst çubuktaki "SES" düğmesi).</summary>
    public static bool SoundOn { get; set; } = true;

    private sealed record Stored(bool AutoStart, double? WidgetLeft = null, double? WidgetTop = null);

    /// <summary>tasarım kararı (2026-10-27): varsayılan açık. Tepsi menüsünden kapatılabilir.</summary>
    public static bool AutoStart
    {
        get => Load()?.AutoStart ?? true;
        set
        {
            Save((Load() ?? new Stored(true)) with { AutoStart = value });
            ApplyAutoStart();
        }
    }

    /// <summary>Masaüstündeki Ultron simgesinin son yeri (sürüklenince kaydedilir); ilk açılışta sağ alt köşe.</summary>
    public static (double? Left, double? Top) WidgetPosition
    {
        get => Load() is { } s ? (s.WidgetLeft, s.WidgetTop) : (null, null);
        set => Save((Load() ?? new Stored(true)) with { WidgetLeft = value.Left, WidgetTop = value.Top });
    }

    /// <summary>
    /// Kayıt defterindeki "Windows açılışında çalıştır" girdisini tercihe göre yazar/siler. Her açılışta çağrılır:
    /// exe başka klasöre taşınırsa yol güncellenir. Ultron tepside, penceresiz başlar (--tray).
    /// </summary>
    public static void ApplyAutoStart()
    {
        try
        {
            // Geliştirme sürümü (bin\Debug) açılınca kurulu sürümün (Setup.exe) açılış kaydını kendine çevirmesin.
            if (IsDevelopmentBuild && File.Exists(InstalledExe))
                return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
                return;
            if (AutoStart && Environment.ProcessPath is { } exe)
                key.SetValue(RunValue, $"\"{exe}\" --tray");
            else if (key.GetValue(RunValue) is not null)
                key.DeleteValue(RunValue);
            DesktopLog.Write($"Windows açılışında başlat: {(AutoStart ? "açık" : "kapalı")}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            DesktopLog.Write("Açılışta başlatma ayarlanamadı: " + ex.Message);
        }
    }

    /// <summary>Setup.exe'nin kurduğu yer (Faz 14).</summary>
    public static readonly string InstalledExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ultron", "Ultron.exe");

    /// <summary>Proje klasöründen (bin\Debug ya da bin\Release) çalışan sürüm mü.</summary>
    public static bool IsDevelopmentBuild =>
        AppContext.BaseDirectory.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
        || AppContext.BaseDirectory.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    private static Stored? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void Save(Stored value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(value));
    }
}
