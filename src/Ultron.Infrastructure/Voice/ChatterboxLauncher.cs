using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ultron.Infrastructure.Voice;

/// <summary>
/// Host açılınca Chatterbox ses servisini (Python) arka planda başlatır, Host kapanınca kapatır.
/// Model yüklenene kadar (ilk açılışta ~30-60 sn) seslendirme Piper ile yapılır.
/// Python ortamı ya da betik yoksa sessizce Piper'da kalınır.
/// </summary>
internal sealed class ChatterboxLauncher(HttpClient http, IOptions<VoiceOptions> options, ILogger<ChatterboxLauncher> logger) : BackgroundService
{
    private Process? _process;
    private CancellationToken _stopping;
    private volatile bool _suspended;

    /// <summary>İş modu (Faz 16): süreç kapatılır, ekran kartı boşalır; bu sırada ses Piper'la (işlemcide) okunur.</summary>
    public bool Suspended => _suspended;

    public Task SuspendAsync()
    {
        _suspended = true;
        if (_process is { HasExited: false } p)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            logger.LogInformation("Chatterbox ekran kartından boşaltıldı (İş modu)");
        }
        return Task.CompletedTask;
    }

    /// <summary>Ana arayüze dönüş: Chatterbox yeniden başlar (model yüklenene kadar ~20-40 sn Piper okur).</summary>
    public void Resume()
    {
        if (!_suspended)
            return;
        _suspended = false;
        _ = Task.Run(() => RunAsync(_stopping), CancellationToken.None);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        return RunAsync(stoppingToken);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        if (_suspended)
            return;
        var o = options.Value;
        if (!string.Equals(o.Engine, "Chatterbox", StringComparison.OrdinalIgnoreCase))
            return;
        // Faz 23: ElevenLabs anahtarı varsa ağır Chatterbox ekran kartına yüklenmez (8 GB'da yer açılsın, diğer modeller hızlansın).
        // ElevenLabs olmazsa ses Piper'a (işlemcide, hafif) düşer.
        if (CloudKeys.Read(ElevenLabsTextToSpeech.KeyFile) is not null)
        {
            logger.LogInformation("ElevenLabs anahtarı var: Chatterbox başlatılmıyor (yedek ses: Piper)");
            return;
        }
        var c = o.Chatterbox;
        if (await IsHealthyAsync(c.Url, stoppingToken))
        {
            // /health cevap verse de ses üretemeyen eski süreç olabilir (2026-10-27: 03:49'dan kalan süreç bağlantıyı kesiyordu,
            // Ultron bütün gün Piper'la konuştu). Kısa bir cümleyle dene; olmazsa eski süreci kapatıp yenisini başlat.
            if (await CanSpeakAsync(c.Url, stoppingToken))
            {
                logger.LogInformation("Chatterbox zaten çalışıyor: {Url}", c.Url);
                return;
            }
            logger.LogWarning("Chatterbox çalışıyor görünüyor ama ses üretemiyor; eski süreç kapatılıp yeniden başlatılıyor.");
            if (!StopStaleServer(new Uri(c.Url).Port, o.ResolveRoot()) || !await WaitUntilDownAsync(c.Url, stoppingToken))
            {
                logger.LogWarning("Eski Chatterbox süreci kapatılamadı; Piper kullanılacak.");
                return;
            }
        }

        var python = c.Python ?? Path.Combine(o.ResolveRoot(), "tts", ".venv", "Scripts", "python.exe");
        var script = c.Script ?? FindScript();
        if (!File.Exists(python) || script is null)
        {
            logger.LogWarning("Chatterbox kurulu değil (python: {Python}, betik: {Script}); Piper kullanılacak.", python, script ?? "bulunamadı");
            return;
        }

        var psi = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(script)!,
        };
        // -I: yalıtılmış mod (kullanıcı site-packages'ı ve çalışma klasöründeki modüller yüklenmez).
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(new Uri(c.Url).Port.ToString(CultureInfo.InvariantCulture));
        var voice = c.ReferenceVoice ?? Path.Combine(o.ResolveRoot(), "voices", "referans.wav");
        if (File.Exists(voice))
        {
            psi.ArgumentList.Add("--voice");
            psi.ArgumentList.Add(voice);
        }
        psi.ArgumentList.Add("--exaggeration");
        psi.ArgumentList.Add(c.Exaggeration.ToString(CultureInfo.InvariantCulture));
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        // Hugging Face model önbelleği de AppData dışında (Claude uygulamasının sanal AppData'sı sorun çıkarıyordu).
        psi.Environment["HF_HOME"] = Path.Combine(o.ResolveRoot(), "models", "huggingface");

        _process = Process.Start(psi);
        if (_process is null)
            return;
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[chatterbox] {Line}", e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogDebug("[chatterbox] {Line}", e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        logger.LogInformation("Chatterbox başlatılıyor (ses: {Voice})", File.Exists(voice) ? voice : "varsayılan");

        try
        {
            await _process.WaitForExitAsync(stoppingToken);
            if (!_suspended)
                logger.LogWarning("Chatterbox kapandı (çıkış kodu {Code}); Piper kullanılacak.", _process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            // Host kapanıyor: StopAsync süreci kapatır.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _process?.Dispose();
        base.Dispose();
    }

    private async Task<bool> IsHealthyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            return (await http.GetAsync(new Uri(new Uri(url), "/health"), timeout.Token)).IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Gerçek bir seslendirme denemesi ("Tamam."): 200 ve içinde ses olan bir WAV dönmeli.</summary>
    internal async Task<bool> CanSpeakAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60)); // başka bir cümle üretiliyorsa sırasını bekler
            using var response = await http.PostAsync(new Uri(new Uri(url), "/tts"), ChatterboxTextToSpeech.Body(new { text = "Tamam." }), timeout.Token);
            return response.IsSuccessStatusCode && (await response.Content.ReadAsByteArrayAsync(timeout.Token)).Length > 44;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Chatterbox ses denemesi başarısız");
            return false;
        }
    }

    /// <summary>
    /// Portu dinleyen süreci kapatır; sadece Ultron'un kendi Python'uysa (%USERPROFILE%\Ultron altında bir python.exe).
    /// Başka bir program aynı portu kullanıyorsa dokunulmaz.
    /// </summary>
    private bool StopStaleServer(int port, string ultronRoot)
    {
        try
        {
            var netstat = Process.Start(new ProcessStartInfo("netstat", "-ano -p TCP") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var output = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(5000);
            if (ParseListeningPid(output, port) is not { } pid)
                return false;
            using var stale = Process.GetProcessById(pid);
            var path = stale.MainModule?.FileName;
            if (!IsUltronPython(path, ultronRoot))
            {
                logger.LogWarning("Port {Port} başka bir programda ({Path}); kapatılmadı.", port, path ?? stale.ProcessName);
                return false;
            }
            stale.Kill(entireProcessTree: true);
            stale.WaitForExit(10_000);
            logger.LogInformation("Eski Chatterbox süreci kapatıldı (PID {Pid}, başlangıç {Start:HH:mm})", pid, stale.StartTime);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Eski Chatterbox süreci kapatılamadı");
            return false;
        }
    }

    /// <summary>"netstat -ano" çıktısında 127.0.0.1:port ya da 0.0.0.0:port'u DİNLEYEN sürecin numarası (dil bağımsız: durum sütununa bakılmaz, karşı adres 0 olmalı).</summary>
    internal static int? ParseListeningPid(string netstat, int port)
    {
        foreach (var line in netstat.Split('\n'))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length >= 5 && cols[0] == "TCP" && cols[1].EndsWith(":" + port, StringComparison.Ordinal)
                && cols[2] is "0.0.0.0:0" or "[::]:0" && int.TryParse(cols[^1], out var pid) && pid > 0)
                return pid;
        }
        return null;
    }

    internal static bool IsUltronPython(string? path, string ultronRoot) =>
        path is not null
        && Path.GetFileName(path).StartsWith("python", StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(ultronRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> WaitUntilDownAsync(string url, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            if (!await IsHealthyAsync(url, cancellationToken))
                return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }

    /// <summary>Geliştirme düzeni: Host'un klasöründen yukarı çıkıp tools\tts-server\server.py aranır.</summary>
    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "tts-server", "server.py");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
