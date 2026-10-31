namespace Ultron.Infrastructure.Persistence;

/// <summary>appsettings.json içindeki "Database" bölümü.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Boşsa %LOCALAPPDATA%\Ultron\ultron.db kullanılır. Bilerek proje klasörünün dışında:
    /// kişisel veriler yanlışlıkla git'e girmesin.
    /// </summary>
    public string? Path { get; set; }

    public string ResolvePath() =>
        string.IsNullOrWhiteSpace(Path)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ultron", "ultron.db")
            : Environment.ExpandEnvironmentVariables(Path);
}
