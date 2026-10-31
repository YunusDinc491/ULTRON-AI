using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Web;

namespace Ultron.Infrastructure.Services;

/// <summary>Açılışta başlatılamayan servisin uyarısı (Host masaüstüne bildirim olarak iletir).</summary>
public interface IServiceWarningNotifier
{
    Task NotifyServiceWarningAsync(string text, CancellationToken cancellationToken = default);
}

internal sealed class LogServiceWarningNotifier(ILogger<LogServiceWarningNotifier> logger) : IServiceWarningNotifier
{
    public Task NotifyServiceWarningAsync(string text, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("SERVİS: {Text}", text);
        return Task.CompletedTask;
    }
}

/// <summary>/durum için açılıştaki servis denetiminin sonucu.</summary>
public sealed class DependencyStatus
{
    public string Ollama { get; internal set; } = "denetleniyor";

    public string Searxng { get; internal set; } = "denetleniyor";

    public string Describe() => $"Ollama: {Ollama} · SearXNG: {Searxng}";
}

/// <summary>
/// Ultron'un dayandığı servisleri açılışta denetler: kapalıysa başlatır, başlatamazsa bildirir (tasarım kararı, 2026-10-27).
/// Bina görevlisi gibi: sabah ışıkları ve asansörü kontrol eder, yanmayanı yakar, tamir gerekiyorsa haber verir.
/// - Ollama (dil modeli): "ollama app.exe" (Ollama'nın kendi tepsi uygulaması) ya da yoksa "ollama serve".
/// - SearXNG (web araması): Docker kapalıysa Docker Desktop başlatılır, sonra "ultron-searxng" kapsayıcısı.
/// Chatterbox'ı ChatterboxLauncher zaten başlatıyor.
/// </summary>
internal sealed class DependencyStarter(
    IHttpClientFactory httpFactory,
    IOptions<OllamaOptions> ollama,
    IOptions<WebOptions> web,
    DependencyStatus status,
    IServiceWarningNotifier notifier,
    ILogger<DependencyStarter> logger) : BackgroundService
{
    public const string SearxngContainer = "ultron-searxng";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // İkisi paralel: Docker'ın açılması 1-2 dk sürebilir, Ollama beklemesin.
        await Task.WhenAll(EnsureOllamaAsync(stoppingToken), EnsureSearxngAsync(stoppingToken));
    }

    private async Task EnsureOllamaAsync(CancellationToken cancellationToken)
    {
        var url = new Uri(ollama.Value.BaseUrl, "api/version");
        if (await IsUpAsync(url, cancellationToken))
        {
            status.Ollama = "çalışıyor";
            return;
        }

        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe");
        var started = File.Exists(app) ? TryStart(app, null) : TryStart("ollama", "serve");
        if (started && await WaitUntilAsync(() => IsUpAsync(url, cancellationToken), TimeSpan.FromSeconds(45), cancellationToken))
        {
            status.Ollama = "Ultron başlattı";
            logger.LogInformation("Ollama kapalıydı, başlatıldı");
            return;
        }
        status.Ollama = "BAŞLATILAMADI";
        await notifier.NotifyServiceWarningAsync(started
            ? "Ollama başlatıldı ama 45 sn içinde cevap vermedi; dil modeli çalışmıyor. Ollama'yı Başlat menüsünden açmayı dene."
            : "Ollama bulunamadı ya da başlatılamadı; Ultron cevap veremez. Ollama'yı Başlat menüsünden aç.", cancellationToken);
    }

    private async Task EnsureSearxngAsync(CancellationToken cancellationToken)
    {
        var url = new Uri(web.Value.SearxngUrl, "healthz");
        if (await IsUpAsync(url, cancellationToken))
        {
            status.Searxng = "çalışıyor";
            return;
        }

        if ((await DockerAsync(["info"], cancellationToken)).Code != 0)
        {
            status.Searxng = "Docker başlatılıyor";
            if (DockerDesktop() is not { } desktop || !TryStart(desktop, null)
                || !await WaitUntilAsync(async () => (await DockerAsync(["info"], cancellationToken)).Code == 0, TimeSpan.FromMinutes(3), cancellationToken))
            {
                await FailSearxngAsync("Docker Desktop açılamadı; web araması kapalı. Docker Desktop'ı elle açıp Ultron'u tepsiden yeniden başlat.", cancellationToken);
                return;
            }
        }

        var (code, output) = await DockerAsync(["start", SearxngContainer], cancellationToken);
        if (code != 0)
        {
            await FailSearxngAsync(output.Contains("No such container", StringComparison.OrdinalIgnoreCase)
                ? "SearXNG kapsayıcısı yok; web araması kapalı. Logo menüsü → Kurulum sihirbazı → Web araması adımından kurabilirsin."
                : "SearXNG kapsayıcısı başlatılamadı; web araması kapalı.", cancellationToken);
            return;
        }
        if (await WaitUntilAsync(() => IsUpAsync(url, cancellationToken), TimeSpan.FromSeconds(60), cancellationToken))
        {
            status.Searxng = "Ultron başlattı";
            logger.LogInformation("SearXNG kapalıydı, başlatıldı");
        }
        else
        {
            await FailSearxngAsync("SearXNG başlatıldı ama cevap vermiyor; web araması kapalı.", cancellationToken);
        }
    }

    private async Task FailSearxngAsync(string message, CancellationToken cancellationToken)
    {
        status.Searxng = "BAŞLATILAMADI";
        await notifier.NotifyServiceWarningAsync(message, cancellationToken);
    }

    internal static string? DockerDesktop() =>
        new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
        }.FirstOrDefault(File.Exists);

    private bool TryStart(string file, string? argument)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            if (argument is not null)
                psi.ArgumentList.Add(argument);
            using var _ = Process.Start(psi); // ayrı süreç: Host kapanınca açık kalır (Ollama/Docker Ultron'a ait değil)
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "{File} başlatılamadı", file);
            return false;
        }
    }

    private async Task<(int Code, string Output)> DockerAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout + await stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (-1, ex.Message);
        }
    }

    private async Task<bool> IsUpAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            using var response = await http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> check, TimeSpan limit, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (await check())
                return true;
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        return await check();
    }
}
