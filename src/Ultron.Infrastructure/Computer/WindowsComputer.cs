using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using Ultron.Core.Computer;
using Ultron.Core.Text;

namespace Ultron.Infrastructure.Computer;

/// <summary>
/// Bilgisayarda gerçekten iş yapan katman: uygulama başlatma (Windows kabuğu), Başlat menüsünde uygulama bulma,
/// geri dönüşüm kutusuna taşıma, komut çalıştırma (cmd, UTF-8, zaman aşımında işlem ağacıyla birlikte durdurulur).
/// </summary>
internal sealed class WindowsComputer(ILogger<WindowsComputer> logger) : IComputer
{
    public void Launch(string command, string? target = null)
    {
        var file = ResolveCommand(command);
        var start = new ProcessStartInfo(file) { UseShellExecute = true };
        if (target is not null)
            start.Arguments = $"\"{target}\"";
        logger.LogInformation("Uygulama başlatılıyor: {File} {Target}", file, target);
        Process.Start(start)?.Dispose();
    }

    /// <summary>"code" PATH'teki code.cmd'dir (kabuk .cmd'yi doğrudan açamaz): kurulu Code.exe'ye çevrilir.</summary>
    internal static string ResolveCommand(string command)
    {
        if (!command.Equals("code", StringComparison.OrdinalIgnoreCase))
            return command;
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists) ?? command;
    }

    public string? FindInstalledApp(string name)
    {
        var key = TurkishText.Normalize(name);
        if (key.Length < 2)
            return null;
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        ];
        var shortcuts = roots.Where(Directory.Exists)
            .SelectMany(r => SafeEnumerate(r))
            .Select(p => (Path: p, Name: TurkishText.Normalize(Path.GetFileNameWithoutExtension(p))))
            // Kaldırma kısayolları ("Uninstall X") hiçbir zaman seçilmesin.
            .Where(s => !s.Name.Contains("uninstall", StringComparison.Ordinal) && !s.Name.Contains("kaldir", StringComparison.Ordinal))
            .ToList();
        return shortcuts.FirstOrDefault(s => s.Name == key).Path
            ?? shortcuts.Where(s => s.Name.StartsWith(key, StringComparison.Ordinal) || s.Name.Contains(" " + key, StringComparison.Ordinal))
                .OrderBy(s => s.Name.Length).FirstOrDefault().Path;
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void Recycle(string path)
    {
        if (Directory.Exists(path))
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    public async Task<CommandResult> RunAsync(string command, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // chcp 65001: Türkçe karakterli çıktı (varsayılan OEM kod sayfası 857) bozulmasın.
        var start = new ProcessStartInfo("cmd.exe", $"/d /s /c \"chcp 65001>nul & {command}\"")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        var gate = new Lock();
        void Add(string? line)
        {
            if (line is null)
                return;
            lock (gate)
            {
                if (output.Length < 20_000)
                    output.AppendLine(line);
            }
        }
        process.OutputDataReceived += (_, e) => Add(e.Data);
        process.ErrorDataReceived += (_, e) => Add(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timer.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
                return new CommandResult(-1, output.ToString().TrimEnd(), TimedOut: true);
        }

        lock (gate)
            return new CommandResult(process.ExitCode, output.ToString().TrimEnd(), TimedOut: false);
    }
}
