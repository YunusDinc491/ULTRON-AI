namespace Ultron.Core.Computer;

/// <summary>Komutun sonucu: çıkış kodu ve (kısaltılmış) çıktı.</summary>
public sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

/// <summary>Bilgisayarda gerçekten iş yapan katman (Windows). Araçlar karar verir, bu sınıf sadece uygular.</summary>
public interface IComputer
{
    /// <summary>Uygulamayı başlatır; <paramref name="target"/> verildiyse onunla (ör. VS Code + proje klasörü).</summary>
    void Launch(string command, string? target = null);

    /// <summary>Başlat menüsünde adı geçen uygulamanın kısayolu ("Spotify" → …\Spotify.lnk); yoksa null.</summary>
    string? FindInstalledApp(string name);

    /// <summary>Klasörü ya da dosyayı Windows'un varsayılan programıyla açar (klasör → Dosya Gezgini).</summary>
    void Open(string path);

    /// <summary>Dosyayı ya da klasörü geri dönüşüm kutusuna taşır (kalıcı silme yok).</summary>
    void Recycle(string path);

    Task<CommandResult> RunAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default);
}
