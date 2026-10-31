using System.Text.Json;
using Ultron.Core.Settings;
using Ultron.Core.Text;

namespace Ultron.Core.Computer;

/// <summary>İzinli uygulama: konuşmada geçen ad ("not defteri") → çalıştırılacak komut ya da yol.</summary>
public sealed record AllowedApp(string Name, string Command);

/// <summary>Süreli izin: <paramref name="Scope"/> (ör. "komut_calistir") bu zamana kadar onaysız (bildirerek) yapılır.</summary>
public sealed record TimedGrant(string Scope, DateTime UntilUtc);

/// <param name="SharedMemory">Claude Code'un (Ultron MCP sunucusundan) görebileceği hafıza kategorileri (anahtar). Varsayılan boş.</param>
public sealed record PermissionSnapshot(IReadOnlyList<AllowedApp> Apps, IReadOnlyList<string> Folders, IReadOnlyList<TimedGrant> Grants, IReadOnlyList<string> SharedMemory);

/// <summary>
/// Bilgisayar yetkileri: izinli uygulamalar, izinli klasörler ve süreli izinler (UserSettings'te tek JSON kaydı).
/// Ev anahtarı listesi gibi: listede olan kapıyı Ultron kendisi açar, olmayan için kullanıcıya sorar.
/// Yetkiyi ARTIRAN değişiklik sadece onay kartından geçen araçla yapılır (bkz. ChangePermissionTool).
/// </summary>
public sealed class ComputerPermissions(UltronSettings settings, TimeProvider time)
{
    public const string SettingKey = "bilgisayar_yetkileri";

    public static string Workspace { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "workspace");

    /// <summary>İlk kurulumda izinli uygulamalar: zararsız, Windows'ta hazır olanlar ve kullanıcının geliştirme araçları.</summary>
    public static readonly AllowedApp[] DefaultApps =
    [
        new("not defteri", "notepad.exe"),
        new("hesap makinesi", "calc.exe"),
        new("paint", "mspaint.exe"),
        new("dosya gezgini", "explorer.exe"),
        new("edge", "msedge.exe"),
        new("vs code", "code"),
        new("visual studio code", "code"),
    ];

    private sealed record Stored(List<AllowedApp> Apps, List<string> Folders, List<TimedGrant> Grants)
    {
        /// <summary>Eski kayıtlarda yok: boş başlar (hiçbir kategori paylaşılmaz).</summary>
        public List<string> SharedMemory { get; init; } = [];
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PermissionSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var s = await LoadAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        return new PermissionSnapshot(s.Apps, s.Folders, s.Grants.Where(g => g.UntilUtc > now).ToList(), s.SharedMemory);
    }

    /// <summary>Adı izin listesinde olan uygulama ("VS Code'u" → "vs code"); yoksa null.</summary>
    public async Task<AllowedApp?> FindAppAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = AppKey(name);
        return (await LoadAsync(cancellationToken)).Apps
            .Where(a => key == AppKey(a.Name) || key.StartsWith(AppKey(a.Name) + " ", StringComparison.Ordinal))
            .MaxBy(a => a.Name.Length);
    }

    public async Task<bool> HasGrantAsync(string scope, CancellationToken cancellationToken = default) =>
        (await GetAsync(cancellationToken)).Grants.Any(g => g.Scope == scope);

    /// <summary>Yol izinli bir klasörün içinde mi (klasörün kendisi dahil)?</summary>
    public async Task<bool> IsAllowedPathAsync(string fullPath, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Folders.Any(f => PathGuard.IsInside(fullPath, f));

    public Task AddAppAsync(string name, string command, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => { s.Apps.RemoveAll(a => AppKey(a.Name) == AppKey(name)); s.Apps.Add(new AllowedApp(name.Trim().ToLower(TurkishCulture), command.Trim())); }, cancellationToken);

    public Task<bool> RemoveAppAsync(string name, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => s.Apps.RemoveAll(a => AppKey(a.Name) == AppKey(name)) > 0, cancellationToken);

    public Task AddFolderAsync(string fullPath, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => { if (!s.Folders.Any(f => PathGuard.Same(f, fullPath))) s.Folders.Add(fullPath); }, cancellationToken);

    public Task<bool> RemoveFolderAsync(string fullPath, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => s.Folders.RemoveAll(f => PathGuard.Same(f, fullPath)) > 0, cancellationToken);

    public Task GrantAsync(string scope, TimeSpan duration, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => { s.Grants.RemoveAll(g => g.Scope == scope); s.Grants.Add(new TimedGrant(scope, time.GetUtcNow().UtcDateTime + duration)); }, cancellationToken);

    public Task<bool> RevokeAsync(string scope, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => s.Grants.RemoveAll(g => g.Scope == scope) > 0, cancellationToken);

    /// <summary>Hafıza kategorisini Claude Code ile paylaş (yetki artırma: sadece onay kartından geçen araç çağırır).</summary>
    public Task ShareMemoryAsync(string categoryKey, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => { if (!s.SharedMemory.Contains(categoryKey, StringComparer.OrdinalIgnoreCase)) s.SharedMemory.Add(categoryKey); }, cancellationToken);

    public Task<bool> UnshareMemoryAsync(string categoryKey, CancellationToken cancellationToken = default) =>
        UpdateAsync(s => s.SharedMemory.RemoveAll(k => k.Equals(categoryKey, StringComparison.OrdinalIgnoreCase)) > 0, cancellationToken);

    private static readonly System.Globalization.CultureInfo TurkishCulture = new("tr-TR");

    private static readonly HashSet<string> Suffixes = ["u", "i", "yu", "yi", "nu", "ni", "a", "e", "ya", "ye", "na", "ne", "yla", "yle", "la", "le"];

    /// <summary>"VS Code'u" → "vs code", "Not Defteri'ni" → "not defteri": ek ve noktalama atılır.</summary>
    internal static string AppKey(string name)
    {
        var words = TurkishText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        // "'u", "'nı" gibi ekler normalleştirmede ayrı kelime olur: 1-3 harflik son eki at.
        if (words.Count > 1 && Suffixes.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    private async Task<Stored> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await settings.GetInternalAsync(SettingKey, cancellationToken);
        Stored? stored = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { stored = JsonSerializer.Deserialize<Stored>(json, Json); }
            catch (JsonException) { }
        }

        return stored ?? new Stored([.. DefaultApps], [Workspace], []);
    }

    private async Task<T> UpdateAsync<T>(Func<Stored, T> change, CancellationToken cancellationToken)
    {
        var s = await LoadAsync(cancellationToken);
        var result = change(s);
        var now = time.GetUtcNow().UtcDateTime;
        s.Grants.RemoveAll(g => g.UntilUtc <= now);
        await settings.SetInternalAsync(SettingKey, JsonSerializer.Serialize(s, Json), cancellationToken);
        return result;
    }

    private Task UpdateAsync(Action<Stored> change, CancellationToken cancellationToken) =>
        UpdateAsync(s => { change(s); return true; }, cancellationToken);
}

/// <summary>Yol güvenliği: izinli klasörden "..\" ile kaçış yok, sistem klasörlerine yazma yasak.</summary>
public static class PathGuard
{
    private static readonly string[] SystemFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    /// <summary>
    /// Kullanıcının söylediği yolu tam yola çevirir. Göreli yol ("notlar.txt", "proje/plan.md") workspace'e göre;
    /// "masaüstü", "belgeler", "indirilenler" kısa adları bilinir. Geçersizse null.
    /// </summary>
    public static string? Resolve(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ComputerPermissions.Workspace;
        var text = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var known = TurkishText.Normalize(text) switch
        {
            "masaustu" or "masaustum" or "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "belgeler" or "belgelerim" or "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "indirilenler" or "downloads" => Path.Combine(home, "Downloads"),
            "workspace" or "calisma alani" or "calisma klasoru" => ComputerPermissions.Workspace,
            _ => null,
        };
        if (known is not null)
            return known;
        if (text.StartsWith('~'))
            text = home + text[1..];
        try
        {
            var full = Path.IsPathRooted(text) ? Path.GetFullPath(text) : Path.GetFullPath(Path.Combine(ComputerPermissions.Workspace, text));
            return full.TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows, Program Files, ProgramData ya da bir sürücünün kökü: yazma ve silme yasak.</summary>
    public static bool IsSystem(string path) =>
        SystemFolders.Any(s => !string.IsNullOrEmpty(s) && IsInside(path, s))
        || Path.GetPathRoot(path) is { } root && Same(path, root);
}
