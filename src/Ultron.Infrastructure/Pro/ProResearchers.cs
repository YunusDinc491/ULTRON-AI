using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Coding;
using Ultron.Core.Pro;
using Ultron.Core.Settings;

namespace Ultron.Infrastructure.Pro;

/// <summary>
/// Ultron Pro'nun araştırmacıları (2026-10-31): Claude Code (bugünkü yol), OpenAI Codex CLI ve Google Gemini CLI. Üçü de kullanıcının
/// kendi hesabıyla, kendi bilgisayarında çalışır; Ultron anahtar saklamaz, aracın kendi girişini kullanır. Ayar "pro_hesap":
/// otomatik (sırayla Claude → Codex → Gemini, hazır olan ilki) ya da biri. Sadece okur: Codex salt-okunur korumalı alanda,
/// Gemini "plan" (değişiklik yapmayan) onay kipinde, Claude sadece WebSearch/WebFetch ile. İstem standart girişten verilir
/// (Windows komut satırında Türkçe karakter ve tırnak bozulmasın).
/// </summary>
internal sealed class ProResearchers(ICodeAgent claude, UltronSettings settings, ILogger<ProResearchers> logger) : IProResearcher
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);
    private static readonly string[] Order = ["claude", "codex", "gemini"];

    internal const string NoneMessage =
        "Ultron Pro için bir hesap bağlı değil. Claude Code, OpenAI Codex CLI ya da Google Gemini CLI'dan birini kurup kendi hesabınla giriş yap " +
        "(ör. \"npm install -g @openai/codex\" sonra \"codex login\"; \"npm install -g @google/gemini-cli\" sonra \"gemini\" ile giriş).";

    public async Task<ProAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var choice = await settings.GetAsync(UltronSettings.ProProvider, cancellationToken);
        if (choice != "otomatik")
            return await StatusAsync(choice, cancellationToken);
        foreach (var provider in Order)
            if (await StatusAsync(provider, cancellationToken) is { Ready: true } ready)
                return ready;
        return new ProAgentStatus(false, "", NoneMessage);
    }

    private async Task<ProAgentStatus> StatusAsync(string provider, CancellationToken cancellationToken)
    {
        switch (provider)
        {
            case "claude":
                var s = await claude.GetStatusAsync(cancellationToken);
                return new ProAgentStatus(s.LoggedIn, "Claude", s.LoggedIn ? "Claude Code hazır." : s.Message);
            case "codex":
                if (FindTool("codex") is not { } codex)
                    return new ProAgentStatus(false, "Codex", "Codex CLI kurulu değil: \"npm install -g @openai/codex\", sonra \"codex login\".");
                var (code, _) = await RunAsync(codex, ["login", "status"], null, null, TimeSpan.FromSeconds(20), cancellationToken);
                return code == 0
                    ? new ProAgentStatus(true, "Codex", "Codex hazır.")
                    : new ProAgentStatus(false, "Codex", "Codex'e giriş yapılmamış: komut satırında \"codex login\".");
            case "gemini":
                if (FindTool("gemini") is null)
                    return new ProAgentStatus(false, "Gemini", "Gemini CLI kurulu değil: \"npm install -g @google/gemini-cli\", sonra \"gemini\" ile giriş.");
                return GeminiLoggedIn()
                    ? new ProAgentStatus(true, "Gemini", "Gemini hazır.")
                    : new ProAgentStatus(false, "Gemini", "Gemini'ye giriş yapılmamış: komut satırında \"gemini\" yazıp Google hesabınla gir.");
            default:
                return new ProAgentStatus(false, "", NoneMessage);
        }
    }

    public async Task<ProAgentResult> RunAsync(string folder, string prompt, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Ready)
            return new ProAgentResult(false, "", Error: status.Message);
        switch (status.Provider)
        {
            case "Claude":
                var r = await claude.RunAsync(new CodeRunRequest(folder, prompt) { AllowedTools = ["WebSearch", "WebFetch"], ReadOnly = true }, progress, cancellationToken);
                return new ProAgentResult(r.Ok, r.Text, r.LimitReached);
            case "Codex":
            {
                var answerFile = Path.Combine(folder, "codex-cevap.md");
                var (code, output) = await RunAsync(FindTool("codex")!,
                    ["exec", "--skip-git-repo-check", "--sandbox", "read-only", "-c", "web_search=\"live\"", "--output-last-message", answerFile, "-"],
                    folder, prompt, Limit, cancellationToken, progress);
                var text = File.Exists(answerFile) ? (await File.ReadAllTextAsync(answerFile, cancellationToken)).Trim() : "";
                return Result(code, text, output, "Codex");
            }
            case "Gemini":
            {
                var (code, output) = await RunAsync(FindTool("gemini")!,
                    ["-p", "Standart girişteki görevi yap.", "--output-format", "json", "--approval-mode", "plan"],
                    folder, prompt, Limit, cancellationToken, progress);
                return Result(code, GeminiAnswer(output), output, "Gemini");
            }
            default:
                return new ProAgentResult(false, "", Error: NoneMessage);
        }
    }

    private ProAgentResult Result(int code, string text, string output, string provider)
    {
        var limit = LooksLikeLimit(output);
        if (code == 0 && text.Length > 0)
            return new ProAgentResult(true, text);
        logger.LogWarning("{Provider} araştırması başarısız (çıkış {Code}): {Output}", provider, code, output.Length > 600 ? output[^600..] : output);
        return new ProAgentResult(false, text, limit, limit ? $"{provider} kullanım limiti doldu." : $"{provider} cevap veremedi (çıkış kodu {code}).");
    }

    /// <summary>Gemini --output-format json: {"response": "...", "stats": {...}}; JSON değilse düz metin.</summary>
    internal static string GeminiAnswer(string output)
    {
        var start = output.IndexOf('{');
        if (start >= 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(output[start..]);
                if (doc.RootElement.TryGetProperty("response", out var response))
                    return (response.GetString() ?? "").Trim();
            }
            catch (JsonException)
            {
            }
        }
        return output.Trim();
    }

    internal static bool LooksLikeLimit(string output)
    {
        var o = output.ToLowerInvariant();
        return o.Contains("rate limit") || o.Contains("usage limit") || o.Contains("quota") || o.Contains("resource_exhausted") || o.Contains("429");
    }

    private static bool GeminiLoggedIn() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "oauth_creds.json"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_API_KEY"));

    /// <summary>PATH'te ve npm'in genel klasöründe (%APPDATA%\npm) "codex"/"gemini" (.exe ya da .cmd).</summary>
    internal static string? FindTool(string name)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        foreach (var dir in dirs)
            foreach (var ext in new[] { ".exe", ".cmd" })
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        return null;
    }

    /// <summary>Aracı çalıştırır; istem standart girişten verilir. Çıkış kodu ve birleşik çıktı (zaman aşımında -1).</summary>
    private static async Task<(int Code, string Output)> RunAsync(string file, IReadOnlyList<string> args, string? folder, string? stdin, TimeSpan limit,
        CancellationToken cancellationToken, Action<string>? progress = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (folder is not null)
            psi.WorkingDirectory = folder;
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, ex.Message);
        }
        using (process)
        {
            var output = new StringBuilder();
            var gate = new object();
            void Collect(string? line)
            {
                if (line is null)
                    return;
                lock (gate)
                    output.AppendLine(line);
                if (progress is not null && line.Trim() is { Length: > 3 } t && t.Any(char.IsLetter))
                    progress(t.Length > 100 ? t[..100] : t);
            }
            process.OutputDataReceived += (_, e) => Collect(e.Data);
            process.ErrorDataReceived += (_, e) => Collect(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.StandardInput.WriteAsync(stdin ?? "");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limit);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (-1, output + "\nzaman aşımı");
            }
            lock (gate)
                return (process.ExitCode, output.ToString());
        }
    }
}
