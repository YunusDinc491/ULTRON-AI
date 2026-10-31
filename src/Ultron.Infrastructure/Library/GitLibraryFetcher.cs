using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Library;

namespace Ultron.Infrastructure.Library;

/// <summary>Kütüphane klasörü: %USERPROFILE%\Ultron\kutuphane (kaynak depoları + düzenlenebilir kaynak listesi).</summary>
public sealed class LibraryOptions
{
    public const string SectionName = "Library";

    public string Folder { get; set; } = "";

    public string ResolveFolder() =>
        string.IsNullOrWhiteSpace(Folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "kutuphane") : Folder;
}

/// <summary>
/// Kaynağı git ile indirir: sığ (sadece son sürüm) ve seyrek (sadece doküman klasörleri) klon — MDN deposunun tamamı GB'larca,
/// sadece web/ klasörleri birkaç yüz MB. Güncellemede sadece son commit çekilir. git yoksa açık bir hata verir.
/// </summary>
internal sealed class GitLibraryFetcher(LibraryOptions options, ILogger<GitLibraryFetcher> logger) : ILibraryFetcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    public async Task<(string Folder, string Commit)> FetchAsync(LibrarySource source, CancellationToken cancellationToken = default)
    {
        // Yerel kaynak ("yerel:notlar/muhendislik"): Ultron'la gelen Markdown notları; git yok, sürüm = dosyaların özeti.
        if (source.Repo.StartsWith(LibrarySources.LocalPrefix, StringComparison.Ordinal))
        {
            var local = Path.Combine(AppContext.BaseDirectory, source.Repo[LibrarySources.LocalPrefix.Length..]);
            if (!Directory.Exists(local))
                throw new DirectoryNotFoundException($"Yerel kaynak klasörü yok: {local}");
            return (local, LocalVersion(local));
        }

        var folder = Path.Combine(options.ResolveFolder(), "kaynaklar", source.Id);
        if (!Directory.Exists(Path.Combine(folder, ".git")))
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true); // yarım kalmış önceki deneme
            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            logger.LogInformation("Kütüphane kaynağı indiriliyor: {Repo} ({Branch})", source.Repo, source.Branch);
            await GitAsync(null, cancellationToken, "clone", "--depth", "1", "--filter=blob:none", "--no-checkout", "--branch", source.Branch,
                $"https://github.com/{source.Repo}.git", folder);
            await GitAsync(folder, cancellationToken, ["sparse-checkout", "set", .. source.Paths]);
            await GitAsync(folder, cancellationToken, "checkout", source.Branch);
        }
        else
        {
            await GitAsync(folder, cancellationToken, ["sparse-checkout", "set", .. source.Paths]); // liste değiştiyse
            await GitAsync(folder, cancellationToken, "fetch", "--depth", "1", "--filter=blob:none", "origin", source.Branch);
            await GitAsync(folder, cancellationToken, "reset", "--hard", "FETCH_HEAD");
        }
        var commit = (await GitAsync(folder, cancellationToken, "rev-parse", "HEAD")).Trim();
        return (folder, commit);
    }

    /// <summary>Klasördeki dosyaların içerik özeti: bir not değişince sürüm değişir ve kaynak yeniden işlenir.</summary>
    internal static string LocalVersion(string folder)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file));
            sha.TransformBlock(name, 0, name.Length, null, 0);
            var content = File.ReadAllBytes(file);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!)[..12].ToLowerInvariant();
    }

    private async Task<string> GitAsync(string? folder, CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (folder is not null)
        {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(folder);
        }
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; // kimlik sormasın (açık depolar)
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git başlatılamadı");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args.Take(2))}: {(await stderr).Trim()}");
        return await stdout;
    }
}

/// <summary>Kaynak listesi dosyası: yoksa varsayılanla yazılır, varsa oradan okunur (kullanıcı kaynak ekleyip çıkarabilsin).</summary>
internal static class LibrarySourceFile
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<LibrarySource> Load(LibraryOptions options, ILogger logger)
    {
        var path = Path.Combine(options.ResolveFolder(), "kutuphane-kaynaklari.json");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(LibrarySources.Default, Json));
                return LibrarySources.Default;
            }
            if (JsonSerializer.Deserialize<List<LibrarySource>>(File.ReadAllText(path), Json) is not { Count: > 0 } list)
                return LibrarySources.Default;
            // Sonraki sürümlerde eklenen varsayılan kaynaklar (ör. 1.0.8'de .NET) dosyada yoksa eklenir; kullanıcının düzenlemeleri korunur.
            var added = MergeDefaults(list);
            if (added.Count > 0)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(list, Json));
                logger.LogInformation("Kütüphaneye yeni kaynaklar eklendi: {Sources}", string.Join(", ", added));
            }
            return list;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Kütüphane kaynak listesi okunamadı, varsayılan kullanılıyor: {Path}", path);
            return LibrarySources.Default;
        }
    }

    /// <summary>Listede kimliği olmayan varsayılan kaynakları sona ekler; eklenenlerin kimliklerini döndürür.</summary>
    internal static IReadOnlyList<string> MergeDefaults(List<LibrarySource> list)
    {
        var known = list.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = LibrarySources.Default.Where(s => !known.Contains(s.Id)).ToList();
        list.AddRange(missing);
        return missing.Select(s => s.Id).ToList();
    }
}
