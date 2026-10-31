using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Ultron.Desktop.Services;

/// <summary>
/// Masaüstü açılırken Ultron.Host çalışmıyorsa başlatır. Geçici çözüm: Faz 14'te Host Windows açılışında
/// kendiliğinden başlayacak ve masaüstü sadece bağlanacak.
/// </summary>
public sealed class HostLauncher(Uri hostUrl, string? configuredExePath) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private Process? _startedByUs;
    private StreamWriter? _hostLog;
    private readonly Lock _hostLogGate = new();

    private void WriteHostLog(string? line)
    {
        if (line is null)
            return;
        lock (_hostLogGate)
        {
            try { _hostLog?.WriteLine(line); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
    }

    /// <summary>Host bizim tarafımızdan mı başlatıldı (çıkışta kapatmak için).</summary>
    public bool StartedHost => _startedByUs is { HasExited: false };

    public async Task<bool> IsRunningAsync()
    {
        try
        {
            return (await _http.GetAsync(new Uri(hostUrl, "/health"))).IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private readonly SemaphoreSlim _ensureGate = new(1, 1);

    /// <summary>
    /// Çalışmıyorsa başlatır ve hazır olana kadar (en fazla 60 sn) bekler. Aynı anda iki yerden çağrılırsa
    /// ikincisi sırasını bekler ve Host'u çalışır bulur: iki Host açılmaz.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureRunningCoreAsync(cancellationToken);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    private async Task EnsureRunningCoreAsync(CancellationToken cancellationToken)
    {
        if (await IsRunningAsync())
            return;

        var exe = FindHostExecutable()
            ?? throw new FileNotFoundException("Ultron.Host.exe bulunamadı. ULTRON_HOST_EXE ortam değişkenini ayarlayın ya da çözümü derleyin (dotnet build Ultron.slnx).");

        // Penceresiz çalıştığı için Host'un konsol çıktısı (STT/TTS ölçümleri, hatalar) her açılışta baştan host.log'a yazılır.
        Directory.CreateDirectory(DesktopLog.Folder);
        _hostLog = new StreamWriter(Path.Combine(DesktopLog.Folder, "host.log"), append: false) { AutoFlush = true };
        _startedByUs = Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, // Host appsettings.json'ı kendi klasöründen okur
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        })!;
        _startedByUs.OutputDataReceived += (_, e) => WriteHostLog(e.Data);
        _startedByUs.ErrorDataReceived += (_, e) => WriteHostLog(e.Data);
        _startedByUs.BeginOutputReadLine();
        _startedByUs.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_startedByUs is { HasExited: true })
                throw new InvalidOperationException($"Ultron.Host başlar başlamaz kapandı (çıkış kodu {_startedByUs.ExitCode}).");
            if (await IsRunningAsync())
                return;
            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Ultron.Host 60 saniyede hazır olmadı.");
    }

    /// <summary>
    /// Ayarda yol varsa onu, sonra kurulu düzeni (Ultron.exe yanında host\Ultron.Host.exe), yoksa geliştirme düzenini dener: Desktop'ın bin klasöründen yukarı çıkıp
    /// src/Ultron.Host/bin/{Debug|Release}/net10.0/Ultron.Host.exe arar.
    /// </summary>
    private string? FindHostExecutable()
    {
        if (!string.IsNullOrWhiteSpace(configuredExePath))
            return File.Exists(configuredExePath) ? configuredExePath : null;

        // Kurulu düzen (Setup.exe, Faz 14): Ultron.exe'nin yanındaki host\ klasörü.
        var installed = Path.Combine(AppContext.BaseDirectory, "host", "Ultron.Host.exe");
        if (File.Exists(installed))
            return installed;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(dir.FullName, "src", "Ultron.Host", "bin", config, "net10.0-windows10.0.19041.0", "Ultron.Host.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Kim başlatmış olursa olsun çalışan bütün Ultron.Host süreçlerini kapatır ("Yeniden başlat" için):
    /// eski bir Host açık kalırsa yeni kod, ayar ve gizli bilgiler (Telegram token'ı) okunmaz.
    /// Chatterbox ses sunucusu ayrı süreç olarak açık kalır; Host yeniden açılınca onu bulup kullanır.
    /// </summary>
    public void StopAllHosts()
    {
        foreach (var host in Process.GetProcessesByName("Ultron.Host"))
        {
            using (host)
            {
                try
                {
                    host.Kill();
                    host.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    DesktopLog.Write($"Host (#{host.Id}) kapatılamadı: {ex.Message}");
                }
            }
        }
        _startedByUs = null;
    }

    public void Dispose()
    {
        if (StartedHost)
        {
            try { _startedByUs!.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        _http.Dispose();
        lock (_hostLogGate)
        {
            _hostLog?.Dispose();
            _hostLog = null;
        }
    }
}
