using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ultron.Core.Backup;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Infrastructure.Backup;

/// <summary>"Backup" bölümü. Boşsa: yedekler %USERPROFILE%\Ultron\yedekler, dosyalar %USERPROFILE%\Ultron'dan.</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string Folder { get; set; } = "";

    /// <summary>Belgeler, ses, ayar dosyalarının kökü (%USERPROFILE%\Ultron).</summary>
    public string DataRoot { get; set; } = "";

    public string ResolveDataRoot() =>
        string.IsNullOrWhiteSpace(DataRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron") : DataRoot;

    public string ResolveFolder() => string.IsNullOrWhiteSpace(Folder) ? Path.Combine(ResolveDataRoot(), "yedekler") : Folder;
}

/// <summary>
/// Tek zip: <c>ultron.db</c> (SQLite çevrimiçi yedek API'si; Host çalışırken de tutarlı kopya, kilitli dosya kopyalanmaz),
/// <c>dosyalar/…</c> (belgeler, referans ses, raporlar, masaüstü ayarları, proje notları) ve <c>yedek.json</c>.
/// Modeller, önbellek, Python ortamı, loglar ve gizli anahtarlar (Telegram token'ı user-secrets'ta) yedeğe girmez.
/// Geri yükleme: önce yedeğin bütünlüğü denetlenir, sonra veritabanı yine yedek API'siyle canlı veritabanının üstüne yazılır
/// (Host'u kapatmaya gerek yok) ve eski sürümden geliyorsa migration'lar uygulanır; dosyalar üzerine yazılır (fazlası silinmez).
/// </summary>
internal sealed partial class ZipBackupArchiver(
    string databasePath,
    string documentsFolder,
    BackupOptions options,
    IDbContextFactory<UltronDbContext> dbFactory,
    ILogger<ZipBackupArchiver> logger) : IBackupArchiver
{
    private const string DbEntry = "ultron.db";
    private const string FilesPrefix = "dosyalar/";
    private const string ManifestEntry = "yedek.json";

    public string Folder => options.ResolveFolder();

    public async Task<BackupFile> CreateAsync(string folder, DateTime nowLocal, string? label, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var name = $"ultron-{nowLocal:yyyyMMdd-HHmmss}{(label is null ? "" : "-" + label)}.zip";
        var target = Path.Combine(folder, name);
        var partial = target + ".yarim";
        var dbCopy = Path.Combine(Path.GetTempPath(), $"ultron-yedek-{Guid.NewGuid():N}.db");
        try
        {
            BackupDatabase(databasePath, dbCopy);
            var files = 0;
            await using (var stream = File.Create(partial))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(dbCopy, DbEntry, CompressionLevel.Optimal);
                foreach (var (entry, path) in FilesToBackup())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        zip.CreateEntryFromFile(path, FilesPrefix + entry, CompressionLevel.Optimal);
                        files++;
                    }
                    catch (IOException ex)
                    {
                        logger.LogDebug(ex, "Yedeğe eklenemedi (kullanımda): {Path}", path);
                    }
                }
                await using var manifest = zip.CreateEntry(ManifestEntry).Open();
                await JsonSerializer.SerializeAsync(manifest, new { surum = 1, tarih = nowLocal, etiket = label, dosya = files }, cancellationToken: cancellationToken);
            }
            File.Move(partial, target, overwrite: true);
            return new BackupFile(target, nowLocal, new FileInfo(target).Length, label);
        }
        finally
        {
            TryDelete(partial);
            TryDelete(dbCopy);
        }
    }

    public IReadOnlyList<BackupFile> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "ultron-*.zip")
            .Select(path => (path, m: FileName().Match(Path.GetFileName(path))))
            .Where(x => x.m.Success)
            .Select(x => new BackupFile(x.path,
                DateTime.ParseExact(x.m.Groups["t"].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
                new FileInfo(x.path).Length,
                x.m.Groups["l"].Success ? x.m.Groups["l"].Value : null))
            .OrderByDescending(b => b.CreatedLocal)
            .ToList();
    }

    public async Task RestoreAsync(BackupFile backup, CancellationToken cancellationToken = default)
    {
        var dbCopy = Path.Combine(Path.GetTempPath(), $"ultron-geri-{Guid.NewGuid():N}.db");
        try
        {
            using (var zip = ZipFile.OpenRead(backup.Path))
            {
                var db = zip.GetEntry(DbEntry) ?? throw new InvalidDataException("Yedekte veritabanı yok.");
                if (zip.GetEntry(ManifestEntry) is null)
                    throw new InvalidDataException("Bu bir Ultron yedeği değil.");
                db.ExtractToFile(dbCopy);
                EnsureIntact(dbCopy);

                // Önce dosyalar (veritabanındaki belge kayıtları bu dosyaları gösterir).
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(FilesPrefix, StringComparison.Ordinal) && e.Length > 0))
                {
                    if (TargetFor(entry.FullName[FilesPrefix.Length..]) is not { } target)
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            BackupDatabase(dbCopy, databasePath);
            SqliteConnection.ClearAllPools();
            await using var context = await dbFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken); // eski sürümün yedeğiyse eksik tablolar eklenir
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbCopy);
        }
    }

    /// <summary>Yedeğe girecek dosyalar: (zip içindeki yol, gerçek yol).</summary>
    internal IEnumerable<(string Entry, string Path)> FilesToBackup()
    {
        var root = options.ResolveDataRoot();
        foreach (var (entry, path) in Roots(root))
        {
            if (File.Exists(path))
                yield return (entry, path);
            else if (Directory.Exists(path))
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    yield return (entry + "/" + Path.GetRelativePath(path, file).Replace('\\', '/'), file);
        }

        // Proje klasörlerinden sadece notlar (kodun kendisi Claude Code'un/git'in işi; node_modules gibi devler girmesin).
        var projects = Path.Combine(root, "projeler");
        if (Directory.Exists(projects))
            foreach (var dir in Directory.EnumerateDirectories(projects))
                foreach (var note in new[] { "ULTRON-NOTLAR.md", "PLAN.md" }.Select(n => Path.Combine(dir, n)).Where(File.Exists))
                    yield return ($"projeler/{Path.GetFileName(dir)}/{Path.GetFileName(note)}", note);
    }

    private IEnumerable<(string Entry, string Path)> Roots(string root) =>
    [
        ("belgeler", documentsFolder),
        ("voices", Path.Combine(root, "voices")),
        ("raporlar", Path.Combine(root, "raporlar")),
        ("desktop-settings.json", Path.Combine(root, "desktop-settings.json")),
    ];

    /// <summary>Zip içindeki yol → geri yüklenecek yer. Bilinen köklerin dışına çıkan ("../") yollar reddedilir.</summary>
    internal string? TargetFor(string entry)
    {
        var root = options.ResolveDataRoot();
        if (entry == "desktop-settings.json")
            return Path.Combine(root, entry);
        var top = entry.Split('/')[0];
        string? baseDir = top switch
        {
            "belgeler" => documentsFolder,
            "voices" or "raporlar" or "projeler" => Path.Combine(root, top),
            _ => null,
        };
        if (baseDir is null || entry.Length <= top.Length + 1)
            return null;
        var target = Path.GetFullPath(Path.Combine(baseDir, entry[(top.Length + 1)..]));
        return target.StartsWith(Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    private static void BackupDatabase(string from, string to)
    {
        using var source = new SqliteConnection($"Data Source={from};Pooling=False");
        using var destination = new SqliteConnection($"Data Source={to};Pooling=False");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void EnsureIntact(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if (command.ExecuteScalar() as string != "ok")
            throw new InvalidDataException("Yedekteki veritabanı bozuk.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    [GeneratedRegex(@"^ultron-(?<t>\d{8}-\d{6})(?:-(?<l>[a-z-]+))?\.zip$")]
    private static partial Regex FileName();
}
