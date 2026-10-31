using System.Text.Json;

namespace Ultron.Core.Tools;

/// <summary>
/// İzin seviyeleri (plan: "İzin sistemi"). Bir araç ne kadar riskliyse o kadar sıkı denetlenir.
/// </summary>
public enum PermissionLevel
{
    /// <summary>Doğrudan yapar: hatırlatıcı, hava durumu, hesap.</summary>
    Serbest = 0,
    /// <summary>Yapar ve "şunu yaptım" der: workspace'te dosya, izinli uygulamayı açma.</summary>
    Bildirerek = 1,
    /// <summary>Önce kullanıcıya sorar: komut çalıştırma, dışarıya mesaj (Faz 12'de onay kartıyla).</summary>
    Onayli = 2,
    /// <summary>Asla yapmaz: sistem ayarları, kalıcı silme, parola, para.</summary>
    Yasak = 3,
}

/// <summary>
/// Araç turunu başlatan kullanıcı mesajı. Araçlar modelin "uydurduğu" bilgiyi kullanıcının gerçekten söyleyip
/// söylemediğini buradan kontrol eder (ör. hatırlatma önemi: kullanıcı söylemediyse sorulur).
/// AsyncLocal: aynı anda iki istek gelse de birbirine karışmaz.
/// </summary>
public static class ToolRunContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? UserText
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

/// <summary>Modele tanıtılan araç: adı, ne işe yaradığı ve parametrelerinin JSON şeması.</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersSchema);

/// <summary>Modelin "şu aracı şu parametrelerle çağır" isteği.</summary>
public sealed record ToolCall(string Name, JsonElement Arguments);

/// <summary>
/// Aracın sonucu. <see cref="ForModel"/> modele geri verilen metin (veri), <see cref="Ok"/> işlemin gerçekten
/// yapılıp yapılmadığı: "kurdum" demeye sadece başarılı bir araç çağrısı hak kazandırır.
/// </summary>
/// <param name="Question">İşlem için kullanıcıdan bilgi gerekiyor: cevap bu soru olur (ör. hatırlatmanın önemi).</param>
public sealed record ToolResult(bool Ok, string ForModel, string? Question = null)
{
    public static ToolResult Success(string forModel) => new(true, forModel);

    public static ToolResult Fail(string reason) => new(false, "HATA: " + reason);

    public static ToolResult NeedInfo(string forModel, string question) => new(false, forModel, question);
}

/// <summary>Ultron'un kullanabileceği bir araç.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }

    PermissionLevel Level { get; }

    /// <summary>
    /// Bu parametrelerle çağrının seviyesi. Çoğu araçta sabit (<see cref="Level"/>); bilgisayar araçlarında yola ve izin
    /// listesine göre değişir (izinli klasöre yazmak bildirerek, dışına yazmak onaylı, sistem klasörüne yazmak yasak).
    /// </summary>
    Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) => Task.FromResult(Level);

    /// <summary>Onay kartında görünen, insan diliyle özet: "Komut çalıştır: dir".</summary>
    string Describe(JsonElement arguments) => $"{Definition.Name}: {arguments.GetRawText()}";

    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>Modelin araçlı bir turdaki cevabı: ya metin (son cevap) ya da araç çağrıları.</summary>
public sealed record ToolTurn(string Content, IReadOnlyList<ToolCall> Calls);

/// <summary>Her araç çağrısının kaydı (denetim kaydı): ne, ne zaman, hangi parametrelerle, sonuç.</summary>
public sealed class ToolAuditEntry
{
    public long Id { get; set; }

    public required string Tool { get; set; }

    public required string Arguments { get; set; }

    public required string Result { get; set; }

    public bool Ok { get; set; }

    public PermissionLevel Level { get; set; }

    /// <summary>Onaylı işlemde kimin onayıyla: "masaüstü", "telegram", "süreli izin"; onay gerekmediyse null.</summary>
    public string? Approval { get; set; }

    public DateTime AtUtc { get; set; }
}

public interface IToolAuditLog
{
    Task AddAsync(ToolAuditEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolAuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
