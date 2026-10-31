namespace Ultron.Core.Documents;

/// <summary>appsettings.json içindeki "Documents" bölümü.</summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>Dosya kopyalarının klasörü. Boşsa %USERPROFILE%\Ultron\belgeler (Claude uygulamasının AppData sanallaştırmasından etkilenmez).</summary>
    public string Folder { get; set; } = "";

    public int MaxFileMegabytes { get; set; } = 50;

    /// <summary>Bir soruya en fazla kaç parça verilir.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Anlamsal benzerlik bunun altındaysa parça alakasız sayılır (hafızadaki soru↔kayıt ölçümüyle aynı eşik).</summary>
    public double MinScore { get; set; } = 0.35;

    public string ResolveFolder() =>
        string.IsNullOrWhiteSpace(Folder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "belgeler")
            : Folder;
}
