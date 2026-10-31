using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ultron.Infrastructure.Web;

/// <summary>JavaScript'le dolan sayfanın son halini (DOM) veren görünmez tarayıcı.</summary>
internal interface IHeadlessBrowser
{
    bool Available { get; }

    Task<string?> RenderAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// Bilgisayardaki Edge'i görünmez çalıştırır (<c>--headless --dump-dom</c>): ek indirme yok. Birçok okul sitesi içeriğini sayfa
/// açıldıktan sonra JavaScript'le yüklüyor; düz indirmede boş görünüyordu. Her açılış geçici, ayrı bir profille (kullanıcının Edge
/// oturumları, çerezleri kullanılmaz) ve en fazla 2 tane aynı anda. Benzetme: perdeyi kapalı bir pencerede sayfayı açıp metnini okumak.
/// </summary>
internal sealed class EdgeHeadlessBrowser(IOptions<WebOptions> options, ILogger<EdgeHeadlessBrowser> logger) : IHeadlessBrowser
{
    private static readonly string[] Candidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
    ];

    private readonly SemaphoreSlim _gate = new(2, 2);
    private string? Path_ => options.Value.EdgePath is { Length: > 0 } p && File.Exists(p) ? p : FindEdge();

    /// <summary>Kurulu msedge.exe (Ultron tarayıcısı da kullanır).</summary>
    public static string? FindEdge() => Candidates.FirstOrDefault(File.Exists);

    public bool Available => Path_ is not null;

    public async Task<string?> RenderAsync(Uri url, CancellationToken cancellationToken)
    {
        if (Path_ is not { } edge || url.Scheme is not ("http" or "https"))
            return null;
        await _gate.WaitAsync(cancellationToken);
        var profile = Path.Combine(Path.GetTempPath(), "ultron-edge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var info = new ProcessStartInfo(edge)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--disable-sync", "--mute-audio", "--hide-scrollbars", $"--user-data-dir={profile}", "--virtual-time-budget=8000",
                "--dump-dom", url.AbsoluteUri,
            })
                info.ArgumentList.Add(arg);

            using var process = Process.Start(info)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var html = await output;
                return html.Length > 200 ? html : null;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (cancellationToken.IsCancellationRequested)
                    throw;
                logger.LogInformation("Görünmez Edge zaman aşımı: {Url}", url);
                return null;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogInformation(ex, "Görünmez Edge açılamadı");
            return null;
        }
        finally
        {
            _gate.Release();
            try { Directory.Delete(profile, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
