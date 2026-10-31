using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Coding;

namespace Ultron.Infrastructure.Coding;

/// <summary>appsettings "ClaudeCode" bölümü: claude.exe yolu (boşsa otomatik bulunur).</summary>
public sealed class ClaudeCodeOptions
{
    public const string SectionName = "ClaudeCode";

    public string? Path { get; set; }

    /// <summary>Ultron MCP sunucusu (Host): Claude Code proje notları ve izinli hafıza için bağlanır. Boşsa bağlanmaz (Cli, testler).</summary>
    public string? McpUrl { get; set; }
}

/// <summary>
/// Claude Code'u headless çalıştırır: <c>claude -p … --output-format stream-json --verbose</c>, proje klasöründe.
/// Dosya düzenlemeleri otomatik kabul (acceptEdits) ama komutlar sadece derleme/test/git listesindekiler (diğerleri reddedilir).
/// Her satır bir JSON olay: "assistant" (metin + araç kullanımı → ilerleme), "result" (son metin, maliyet, oturum).
/// Ham günlük %USERPROFILE%\Ultron\logs\kodlama\ altına yazılır.
/// </summary>
internal sealed class ClaudeCodeAgent(Microsoft.Extensions.Options.IOptions<ClaudeCodeOptions> options, ILogger<ClaudeCodeAgent> logger) : ICodeAgent
{
    /// <summary>Claude Code'un onaysız çalıştırabileceği komutlar: derleme, test, paket, git (push hariç).</summary>
    internal static readonly string[] AllowedTools =
    [
        "Read", "Edit", "Write", "Glob", "Grep", "TodoWrite",
        "Bash(dotnet:*)", "Bash(npm:*)", "Bash(npx:*)", "Bash(node:*)", "Bash(python:*)", "Bash(py:*)", "Bash(pip:*)",
        "Bash(git init:*)", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git add:*)", "Bash(git commit:*)", "Bash(git log:*)",
        "Bash(mkdir:*)", "Bash(ls:*)", "Bash(dir:*)", "Bash(cat:*)", "Bash(type:*)",
    ];

    /// <summary>Ultron MCP araçları (onaysız: sadece bu projenin notları ve kullanıcının paylaştığı hafıza).</summary>
    internal static readonly string[] McpTools = ["mcp__ultron__proje_bilgisi", "mcp__ultron__proje_notu_ekle", "mcp__ultron__kullanici_tercihleri"];

    internal static string McpConfig(string url, string token) =>
        System.Text.Json.JsonSerializer.Serialize(new { mcpServers = new { ultron = new { type = "http", url, headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token } } } });

    private static readonly string LogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "logs", "kodlama");

    private (CodeAgentStatus Status, DateTime At)? _cached;

    /// <summary>Durum 30 sn önbellekte (/durum her açıldığında claude.exe başlatılmasın).</summary>
    public async Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } c && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30))
            return c.Status;
        var status = await ReadStatusAsync(cancellationToken);
        _cached = (status, DateTime.UtcNow);
        return status;
    }

    private async Task<CodeAgentStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        if (FindExecutable() is not { } exe)
            return new CodeAgentStatus(null, false, "Claude Code bulunamadı. Kurulum: PowerShell'de  irm https://claude.ai/install.ps1 | iex  , sonra  claude  ile giriş yap.");
        try
        {
            var start = new ProcessStartInfo(exe, "auth status") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var loggedIn = output.Contains("\"loggedIn\": true", StringComparison.Ordinal) || output.Contains("\"loggedIn\":true", StringComparison.Ordinal);
            return new CodeAgentStatus(exe, loggedIn, loggedIn
                ? "hazır (" + exe + ")"
                : "GİRİŞ YAPILMADI: terminalde  claude  yazıp /login ile Claude aboneliğinle giriş yap.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CodeAgentStatus(exe, false, "Claude Code çalıştırılamadı: " + ex.Message);
        }
    }

    /// <summary>Ayar → PATH'teki claude → ~/.local/bin → Claude masaüstü uygulamasının kopyası (en yeni sürüm).</summary>
    internal string? FindExecutable()
    {
        if (options.Value.Path is { Length: > 0 } configured && File.Exists(configured))
            return configured;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "claude.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(local))
            return local;
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
        if (!Directory.Exists(bundled))
            return null;
        return Directory.EnumerateFiles(bundled, "claude.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public async Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var exe = FindExecutable() ?? throw new InvalidOperationException("Claude Code bulunamadı.");
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = request.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(request.Prompt);
        start.ArgumentList.Add("--output-format");
        start.ArgumentList.Add("stream-json");
        start.ArgumentList.Add("--verbose");
        start.ArgumentList.Add("--permission-mode");
        start.ArgumentList.Add(request.ReadOnly ? "default" : "acceptEdits"); // -p modunda izinsiz araç sorulamaz, reddedilir
        start.ArgumentList.Add("--allowedTools");
        var tools = request.AllowedTools ?? (request.McpToken is null ? AllowedTools : [.. AllowedTools, .. McpTools]);
        start.ArgumentList.Add(string.Join(",", tools));
        foreach (var folder in request.ExtraFolders)
        {
            start.ArgumentList.Add("--add-dir");
            start.ArgumentList.Add(folder);
        }
        // Ultron MCP: bu çalıştırmaya özel anahtarla; kullanıcının başka MCP sunucuları yüklenmez (--strict-mcp-config).
        string? mcpConfig = null;
        if (request.McpToken is { } token && options.Value.McpUrl is { Length: > 0 } mcpUrl)
        {
            Directory.CreateDirectory(LogFolder);
            mcpConfig = System.IO.Path.Combine(LogFolder, $"mcp-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(mcpConfig, McpConfig(mcpUrl, token), cancellationToken);
            start.ArgumentList.Add("--mcp-config");
            start.ArgumentList.Add(mcpConfig);
            start.ArgumentList.Add("--strict-mcp-config");
        }

        if (request.ResumeSessionId is { } session)
        {
            start.ArgumentList.Add("--resume");
            start.ArgumentList.Add(session);
        }

        Directory.CreateDirectory(LogFolder);
        var logPath = Path.Combine(LogFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(request.Folder)}.jsonl");
        await using var log = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
        logger.LogInformation("Claude Code başlıyor: {Folder} (devam: {Session}) günlük: {Log}", request.Folder, request.ResumeSessionId ?? "-", logPath);

        using var process = Process.Start(start)!;
        process.StandardInput.Close(); // istem argümanla verildi; Claude stdin beklemesin
        using var kill = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var parser = new StreamJsonParser();
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            await log.WriteLineAsync(line);
            if (parser.Feed(line) is { } step)
                progress(step);
        }

        await process.WaitForExitAsync(CancellationToken.None);
        if (mcpConfig is not null)
            File.Delete(mcpConfig); // anahtar zaten iş bitince geçersiz; dosya da kalmasın
        cancellationToken.ThrowIfCancellationRequested();
        var errors = await stderr;
        if (errors.Length > 0)
            await log.WriteLineAsync("STDERR: " + errors);

        return parser.Result(process.ExitCode, errors);
    }
}

/// <summary>Claude Code stream-json satırlarını okur: ilerleme cümleleri ve son sonuç.</summary>
internal sealed class StreamJsonParser
{
    private string? _sessionId;
    private string? _resultText;
    private string _lastText = "";
    private bool _isError;
    private double _cost;

    /// <returns>Arayüzde gösterilecek ilerleme satırı (yoksa null).</returns>
    public string? Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
            return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String)
                _sessionId = s.GetString();
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "system" when root.TryGetProperty("subtype", out var st) && st.GetString() == "init":
                    return "Claude Code başladı";
                case "assistant" when root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                    string? step = null;
                    foreach (var item in content.EnumerateArray())
                    {
                        var kind = item.TryGetProperty("type", out var k) ? k.GetString() : null;
                        if (kind == "text" && item.TryGetProperty("text", out var text))
                            _lastText = text.GetString() ?? _lastText;
                        else if (kind == "tool_use")
                            step = Describe(item);
                    }
                    return step;
                case "result":
                    _resultText = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                    _isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True
                        || root.TryGetProperty("subtype", out var sub) && sub.GetString() is { } subtype && subtype.StartsWith("error", StringComparison.Ordinal);
                    _cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0;
                    return null;
            }
        }

        return null;
    }

    /// <summary>Araç kullanımı → Türkçe ilerleme: "Düzenliyor: Program.cs", "Komut: dotnet build".</summary>
    internal static string? Describe(JsonElement toolUse)
    {
        var name = toolUse.TryGetProperty("name", out var n) ? n.GetString() : null;
        var input = toolUse.TryGetProperty("input", out var i) ? i : default;
        string? Arg(string key) => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var file = Arg("file_path") is { } f ? Path.GetFileName(f) : null;
        return name switch
        {
            "Write" => $"Dosya yazıyor: {file}",
            "Edit" or "MultiEdit" => $"Düzenliyor: {file}",
            "Read" => $"Okuyor: {file}",
            "Bash" or "PowerShell" => $"Komut: {Clip(Arg("command"))}",
            "Glob" or "Grep" => "Projede arıyor",
            "TodoWrite" => "Yapılacaklar listesini güncelliyor",
            "WebSearch" or "WebFetch" => "Web'e bakıyor",
            "mcp__ultron__proje_bilgisi" => "Ultron'dan proje bilgisini alıyor",
            "mcp__ultron__kullanici_tercihleri" => "Paylaştığın tercihlere bakıyor",
            "mcp__ultron__proje_notu_ekle" => "Proje notlarına karar yazıyor",
            null => null,
            _ => $"Araç: {name}",
        };
    }

    private static string Clip(string? text) => text is null ? "" : text.Length > 80 ? text[..80] + "…" : text;

    public CodeRunResult Result(int exitCode, string stderr)
    {
        var text = _resultText ?? _lastText;
        if (string.IsNullOrWhiteSpace(text))
            text = stderr.Length > 0 ? stderr.Trim() : $"Claude Code sonuç vermeden bitti (çıkış kodu {exitCode}).";
        var lower = text.ToLowerInvariant();
        // Abonelik kullanım limiti: iş "Bekliyor" olur, limit yenilenince "devam et".
        var limit = lower.Contains("usage limit", StringComparison.Ordinal) || lower.Contains("limit reached", StringComparison.Ordinal)
            || lower.Contains("rate limit", StringComparison.Ordinal) || lower.Contains("limit will reset", StringComparison.Ordinal);
        var notLoggedIn = lower.Contains("not logged in", StringComparison.Ordinal) || lower.Contains("/login", StringComparison.Ordinal);
        if (notLoggedIn)
            text = "Claude Code'a giriş yapılmamış: terminalde  claude  yazıp /login ile Claude aboneliğinle giriş yap.";
        return new CodeRunResult(!_isError && !limit && !notLoggedIn && exitCode == 0, text, _sessionId, _cost, limit);
    }
}
