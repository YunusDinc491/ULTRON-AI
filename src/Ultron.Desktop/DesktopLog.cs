using System.IO;

namespace Ultron.Desktop;

/// <summary>
/// Masaüstü uygulamasının basit günlüğü: %USERPROFILE%\Ultron\logs\desktop.log.
/// Pencere/kısayol/tepsi/ses gibi ekranda görünmeyen olayları sonradan inceleyebilmek için.
/// AppData değil: Claude uygulamasının içinden bakınca AppData sanal bir kopya; gerçek günlük görünmüyordu.
/// </summary>
internal static class DesktopLog
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "logs");

    private static readonly string FilePath = Path.Combine(Folder, "desktop.log");

    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Günlük yazılamazsa uygulama çalışmaya devam etmeli.
        }
    }
}
