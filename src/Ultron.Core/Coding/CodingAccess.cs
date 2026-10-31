using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;

namespace Ultron.Core.Coding;

/// <summary>
/// Ultron MCP sunucusunun anahtarları: her Claude Code çalıştırmasına o işe özel, tahmin edilemez bir anahtar verilir;
/// iş bitince geçersiz olur. Otel kartı gibi: sadece kendi odasını (projesini) açar, çıkışta iptal edilir.
/// Böylece bu bilgisayardaki başka bir program 127.0.0.1'deki MCP uç noktasından hafızayı okuyamaz.
/// </summary>
public sealed class CodingAccess
{
    private readonly ConcurrentDictionary<string, long> _tokens = new(StringComparer.Ordinal);

    public string Issue(long jobId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = jobId;
        return token;
    }

    public long? Resolve(string? token) =>
        token is { Length: > 0 } && _tokens.TryGetValue(token, out var id) ? id : null;

    public void Revoke(string token) => _tokens.TryRemove(token, out _);
}

/// <summary>Proje notları: proje klasöründe ULTRON-NOTLAR.md (Claude'un kararları, ilerleme; kullanıcı da okuyabilir).</summary>
public static class ProjectNotes
{
    public const string FileName = "ULTRON-NOTLAR.md";
    private const int MaxChars = 6000;

    public static string Path(string folder) => System.IO.Path.Combine(folder, FileName);

    public static string Read(string folder)
    {
        var path = Path(folder);
        if (!File.Exists(path))
            return "";
        var text = File.ReadAllText(path);
        return text.Length > MaxChars ? "…\n" + text[^MaxChars..] : text; // en yeni notlar sonda
    }

    public static async Task AppendAsync(string folder, string kind, string note, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var path = Path(folder);
        var header = File.Exists(path) ? "" : "# Ultron proje notları\n\n_Claude Code'un kararları ve ilerlemesi (Ultron MCP aracı `proje_notu_ekle`)._\n\n";
        var line = $"- {now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} · **{kind}** · {note.Trim().ReplaceLineEndings(" ")}\n";
        await File.AppendAllTextAsync(path, header + line, cancellationToken);
    }
}
