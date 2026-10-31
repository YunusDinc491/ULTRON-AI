using System.Text;
using System.Text.Json;
using Ultron.Core.Tools;

namespace Ultron.Core.Backup;

/// <summary>"Şimdi yedek al".</summary>
public sealed class BackupNowTool(BackupService backups) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "yedek_al",
        "Ultron'un verilerinin (hafıza, konuşmalar, hatırlatmalar, ayarlar, belgeler, referans ses) şimdi yedeğini alır. Yedekler her gün 03:00'te zaten otomatik alınır.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        await backups.BackupNowAsync(cancellationToken: cancellationToken) is { } file
            ? ToolResult.Success($"Yedek alındı: {file.Name} ({BackupService.Size(file.Bytes)}). {await backups.DescribeAsync(cancellationToken)}")
            : ToolResult.Fail("Yedek alınamadı: " + backups.LastError);
}

public sealed class ListBackupsTool(BackupService backups) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "yedekleri_listele",
        "Alınmış yedekleri (tarih, boyut) ve yedekleme durumunu listeler.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var all = backups.List();
        var sb = new StringBuilder($"Yedekleme: {await backups.DescribeAsync(cancellationToken)}\n");
        foreach (var b in all.Take(15))
            sb.AppendLine($"- {b.CreatedLocal:dd.MM.yyyy HH:mm} · {BackupService.Size(b.Bytes)}{(b.Label is null ? "" : " · geri yükleme öncesi")}");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

/// <summary>"Dünkü yedekten geri yükle". Onaylı: önce mevcut durum yedeklenir, sonra seçilen yedek yüklenir.</summary>
public sealed class RestoreBackupTool(BackupService backups) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "yedekten_geri_yukle",
        "Ultron'un verilerini bir yedekten geri yükler (hafıza, konuşmalar, ayarlar, belgeler). Önce mevcut durum ayrıca yedeklenir. Her zaman kullanıcı onayı ister.",
        """
        {"type":"object","properties":{
          "hangisi":{"type":"string","description":"en_son, dun, bugun ya da GG.AA tarihi (ör. 25.10)"}
        },"required":["hangisi"]}
        """);

    public PermissionLevel Level => PermissionLevel.Onayli;

    public string Describe(JsonElement arguments) =>
        backups.Find(ToolArgs.String(arguments, "hangisi") ?? "") is { } b
            ? $"Yedekten geri yükle: {b.CreatedLocal:dd.MM.yyyy HH:mm} ({BackupService.Size(b.Bytes)}). Şu anki veriler bu yedekteki hâline döner; mevcut durum önce ayrıca yedeklenir."
            : "Yedekten geri yükle (yedek bulunamadı)";

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (backups.Find(ToolArgs.String(arguments, "hangisi") ?? "") is not { } backup)
            return ToolResult.Fail("Bu tarihte yedek yok. Yedekler: " + string.Join(", ", backups.List().Where(b => b.Label is null).Take(8).Select(b => b.CreatedLocal.ToString("dd.MM HH:mm"))));
        try
        {
            var safety = await backups.RestoreAsync(backup, cancellationToken);
            return ToolResult.Success($"{backup.CreatedLocal:dd.MM.yyyy HH:mm} yedeği geri yüklendi. Geri yüklemeden önceki durum da yedeklendi ({safety.Name}); istersen ona geri dönebilirsin.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return ToolResult.Fail("Geri yüklenemedi: " + ex.Message);
        }
    }
}
