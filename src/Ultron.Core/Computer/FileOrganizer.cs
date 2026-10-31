using System.Text.Json;
using Ultron.Core.Text;

namespace Ultron.Core.Computer;

/// <summary>Bir taşıma: nereden, nereye (geri almak için kaydedilir).</summary>
public sealed record FileMove(string From, string To);

/// <summary>Bir toplu iş: başlık ve taşımalar. "Geri al" son işi tersine çevirir.</summary>
public sealed record FileBatch(string Id, DateTime AtUtc, string Title, IReadOnlyList<FileMove> Moves);

/// <summary>PDF birleştirme/bölme (Infrastructure'da PdfPig ile).</summary>
public interface IPdfTools
{
    /// <returns>Çıktı dosyasının sayfa sayısı.</returns>
    int Merge(IReadOnlyList<string> inputs, string output);

    int Split(string input, IReadOnlyList<int> pages, string output);

    int PageCount(string input);
}

/// <summary>
/// Dosya düzenleme (Faz 21, tasarım kararı): İndirilenler, Masaüstü, Belgeler (ve Ultron'un çalışma alanı). Toplu işte önce plan
/// gösterilir, onaylanınca yapılır; her taşıma kaydedilir, "geri al" son işi eski haline döndürür. Silme yok (o ayrı, onaylı araç).
/// Benzetme: dolabı düzenleyen yardımcı önce "şunları şu çekmeceye koyacağım" der, sonra yapar, fotoğrafını da çeker.
/// </summary>
public sealed class FileOrganizer(TimeProvider time, string? logPath = null)
{
    private readonly string _logPath = logPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "dosya-islemleri.json");
    private readonly object _gate = new();

    public static IReadOnlyList<string> Roots { get; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ComputerPermissions.Workspace,
    ];

    /// <summary>Test ve özel kurulum için: izinli kökler dışarıdan verilebilir.</summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = Roots;

    public bool IsAllowed(string path) => AllowedRoots.Any(r => !string.IsNullOrEmpty(r) && (PathGuard.IsInside(path, r) || string.Equals(path.TrimEnd('\\'), r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));

    /// <summary>"indirilenlerdeki", "masaüstümde", "belgelerimdeki" → klasör; tam yol da olur.</summary>
    public static string? ResolveFolder(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var n = TurkishText.Normalize(input);
        if (n.StartsWith("indirilen", StringComparison.Ordinal) || n.StartsWith("download", StringComparison.Ordinal))
            return Roots[0];
        if (n.StartsWith("masaust", StringComparison.Ordinal) || n.StartsWith("desktop", StringComparison.Ordinal))
            return Roots[1];
        if (n.StartsWith("belgeler", StringComparison.Ordinal) || n.StartsWith("dokuman", StringComparison.Ordinal) || n.StartsWith("documents", StringComparison.Ordinal))
            return Roots[2];
        return PathGuard.Resolve(input);
    }

    // ---- Türler ----

    private static readonly (string Folder, string[] Extensions)[] Kinds =
    [
        ("Belgeler", [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".odt", ".rtf", ".csv"]),
        ("Resimler", [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".svg"]),
        ("Videolar", [".mp4", ".mkv", ".mov", ".avi", ".webm"]),
        ("Müzik", [".mp3", ".wav", ".flac", ".m4a", ".ogg"]),
        ("Arşivler", [".zip", ".rar", ".7z", ".tar", ".gz"]),
        ("Kurulumlar", [".exe", ".msi", ".msix", ".appx"]),
        ("Kod", [".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".html", ".css", ".json", ".xml", ".sql", ".ipynb"]),
    ];

    private static readonly Dictionary<string, string[]> KindWords = new()
    {
        ["pdf"] = [".pdf"], ["belge"] = Kinds[0].Extensions, ["word"] = [".doc", ".docx"], ["excel"] = [".xls", ".xlsx", ".csv"],
        ["sunum"] = [".ppt", ".pptx"], ["powerpoint"] = [".ppt", ".pptx"], ["resim"] = Kinds[1].Extensions, ["fotograf"] = Kinds[1].Extensions,
        ["video"] = Kinds[2].Extensions, ["muzik"] = Kinds[3].Extensions, ["arsiv"] = Kinds[4].Extensions, ["zip"] = Kinds[4].Extensions,
        ["kurulum"] = Kinds[5].Extensions, ["kod"] = Kinds[6].Extensions,
    };

    private static readonly string[] InProgress = [".crdownload", ".part", ".tmp", ".download", ".partial"];

    public static string KindFolder(string file) =>
        Kinds.FirstOrDefault(k => k.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())).Folder ?? "Diğer";

    // ---- Bulma ----

    /// <summary>Ad parçası, tür ("pdf", "resim") ve son N gün; en yeni önce, en fazla 30.</summary>
    public IReadOnlyList<FileInfo> Find(string? name, string? kind, int? lastDays, string? folder)
    {
        var roots = ResolveFolder(folder) is { } f && IsAllowed(f) ? [f] : AllowedRoots.Where(Directory.Exists).ToList();
        string[]? extensions = kind is null ? null : KindWords.FirstOrDefault(k => TurkishText.Normalize(kind).StartsWith(k.Key, StringComparison.Ordinal)).Value
            ?? (kind.StartsWith('.') ? [kind.ToLowerInvariant()] : null);
        var words = name is null ? [] : TurkishText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var since = lastDays is { } d ? time.GetUtcNow().UtcDateTime.AddDays(-d) : (DateTime?)null;
        return roots.SelectMany(r => Enumerate(r, depth: 3))
            .Select(p => new FileInfo(p))
            .Where(f => extensions is null || extensions.Contains(f.Extension.ToLowerInvariant()))
            .Where(f => since is null || f.LastWriteTimeUtc >= since)
            .Where(f => words.Length == 0 || words.All(w => TurkishText.Normalize(Path.GetFileNameWithoutExtension(f.Name)).Contains(w, StringComparison.Ordinal)))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(30).ToList();
    }

    private static IEnumerable<string> Enumerate(string root, int depth)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }
        foreach (var f in files)
            yield return f;
        if (depth <= 0)
            yield break;
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root).Where(d => !Path.GetFileName(d).StartsWith('.') && (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }
        foreach (var d in dirs)
            foreach (var f in Enumerate(d, depth - 1))
                yield return f;
    }

    // ---- Toplu düzenleme planı ----

    /// <summary>
    /// Klasörün üst düzeyindeki dosyalar türüne ("Belgeler", "Resimler"…) ya da ayına ("2026-10") göre alt klasörlere. İnmekte
    /// olan ve son 2 dakikada değişen dosyalara dokunulmaz.
    /// </summary>
    public IReadOnlyList<FileMove> Plan(string folder, string rule)
    {
        if (!Directory.Exists(folder))
            return [];
        var cutoff = time.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var byDate = TurkishText.Normalize(rule).StartsWith("tarih", StringComparison.Ordinal) || rule.StartsWith("ay", StringComparison.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folder)
            .Select(p => new FileInfo(p))
            .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !InProgress.Contains(f.Extension.ToLowerInvariant())
                && f.LastWriteTimeUtc < cutoff && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && f.Extension != ".lnk")
            .Select(f => new FileMove(f.FullName, Path.Combine(folder, byDate ? f.LastWriteTime.ToString("yyyy-MM") : KindFolder(f.Name), f.Name)))
            .ToList();
    }

    public static string Summary(string folder, IReadOnlyList<FileMove> moves)
    {
        if (moves.Count == 0)
            return $"{folder}: düzenlenecek dosya yok.";
        var groups = moves.GroupBy(m => Path.GetFileName(Path.GetDirectoryName(m.To)!)).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()})");
        return $"{folder}: {moves.Count} dosya alt klasörlere → {string.Join(", ", groups)}";
    }

    // ---- Uygulama ve geri alma ----

    /// <summary>Taşımaları yapar (aynı adda dosya varsa "ad (2).uzantı"), işi kaydeder. Yapılan taşımaları döner.</summary>
    public FileBatch Apply(string title, IReadOnlyList<FileMove> moves)
    {
        var done = new List<FileMove>();
        foreach (var m in moves)
        {
            if (!File.Exists(m.From) && !Directory.Exists(m.From))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(m.To)!);
            var target = Unique(m.To);
            if (Directory.Exists(m.From))
                Directory.Move(m.From, target);
            else
                File.Move(m.From, target);
            done.Add(new FileMove(m.From, target));
        }
        var batch = new FileBatch(Guid.NewGuid().ToString("N")[..8], time.GetUtcNow().UtcDateTime, title, done);
        if (done.Count > 0)
            Save(Load().Prepend(batch).Take(20).ToList());
        return batch;
    }

    /// <summary>Son işi geri alır: taşınan dosyalar eski yerine (orası boşsa) döner, boşalan klasörler silinmez.</summary>
    public (FileBatch? Batch, int Restored, int Skipped) UndoLast()
    {
        var log = Load();
        if (log.Count == 0)
            return (null, 0, 0);
        var batch = log[0];
        int restored = 0, skipped = 0;
        foreach (var m in batch.Moves.Reverse())
        {
            if ((File.Exists(m.To) || Directory.Exists(m.To)) && !File.Exists(m.From) && !Directory.Exists(m.From))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m.From)!);
                if (Directory.Exists(m.To))
                    Directory.Move(m.To, m.From);
                else
                    File.Move(m.To, m.From);
                restored++;
            }
            else
                skipped++;
        }
        Save(log.Skip(1).ToList());
        return (batch, restored, skipped);
    }

    internal static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private List<FileBatch> Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_logPath) ? JsonSerializer.Deserialize<List<FileBatch>>(File.ReadAllText(_logPath), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return [];
            }
        }
    }

    private void Save(List<FileBatch> log)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.WriteAllText(_logPath, JsonSerializer.Serialize(log, Json));
        }
    }

    /// <summary>"1-3, 5, 8-9" → [1,2,3,5,8,9] (sayfa sayısıyla sınırlı).</summary>
    public static IReadOnlyList<int> ParsePages(string spec, int pageCount)
    {
        var pages = new List<int>();
        foreach (var part in spec.Replace(" ", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (range.Length == 1 && int.TryParse(range[0], out var single))
                pages.Add(single);
            else if (range.Length == 2 && int.TryParse(range[0], out var a) && (int.TryParse(range[1], out var b) || (range[1].Length == 0 && (b = pageCount) > 0)))
                pages.AddRange(Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1));
        }
        return pages.Where(p => p >= 1 && p <= pageCount).Distinct().ToList();
    }
}
