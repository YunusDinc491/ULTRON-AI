using System.Text;
using System.Text.Json;
using Ultron.Core.Computer;
using Ultron.Core.Tools;

namespace Ultron.Core.Coding;

/// <summary>"Hava durumu gösteren bir web sitesi yapalım" → onay kartı → Claude Code'a devredilir (her zaman onaylı: kod bulutta yazılır).</summary>
public sealed class StartCodingTool(CodingService coding) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kodlama_baslat",
        "Bir yazılım işini (uygulama, web sitesi, betik, oyun, kod düzeltme) Claude Code'a devreder: proje klasörüne PLAN.md yazılır, Claude Code kodu yazar ve dener. Kullanıcı onayı ister.",
        """
        {"type":"object","properties":{
          "proje":{"type":"string","description":"Kısa proje adı (ör. 'hava durumu sitesi')"},
          "istek":{"type":"string","description":"Ne yapılacağı, kullanıcının söylediği tüm ayrıntılarla (özellikler, teknoloji, tasarım)"},
          "klasor":{"type":"string","description":"İsteğe bağlı: mevcut bir projenin tam klasör yolu"}
        },"required":["proje","istek"]}
        """);

    public PermissionLevel Level => PermissionLevel.Onayli;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(PathGuard.IsSystem(CodingService.FolderFor(ToolArgs.String(arguments, "proje") ?? "proje", ToolArgs.String(arguments, "klasor")))
            ? PermissionLevel.Yasak
            : PermissionLevel.Onayli);

    public string Describe(JsonElement arguments)
    {
        var request = ToolArgs.String(arguments, "istek") ?? "";
        return $"Claude Code'a kodlama işi ver (proje kodu ve PLAN.md buluta gider, kişisel hafıza gitmez)\n" +
               $"Klasör: {CodingService.FolderFor(ToolArgs.String(arguments, "proje") ?? "proje", ToolArgs.String(arguments, "klasor"))}\n" +
               $"İstek: {(request.Length > 300 ? request[..300] + "…" : request)}";
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgs.String(arguments, "proje") is not { } project || ToolArgs.String(arguments, "istek") is not { } request)
            return ToolResult.Fail("'proje' ve 'istek' gerekli.");
        var status = await coding.AgentStatusAsync(cancellationToken);
        if (status.Executable is null || !status.LoggedIn)
            return ToolResult.Fail(status.Message);
        try
        {
            var job = await coding.StartAsync(project, ToolArgs.String(arguments, "klasor"), request, cancellationToken);
            return ToolResult.Success($"#{job.Id} numaralı kodlama işi başladı ({job.Folder}). Claude Code arka planda çalışıyor; bitince ya da soru sorarsa haber vereceğim.");
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }
}

/// <summary>"Kodlama ne durumda?"</summary>
public sealed class CodingStatusTool(CodingService coding) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kodlama_durum",
        "Claude Code'a verilen kodlama işlerinin durumunu, son ilerlemeyi ve son mesajı (özet ya da soru) gösterir.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var jobs = await coding.RecentAsync(3, cancellationToken);
        if (jobs.Count == 0)
        {
            var status = await coding.AgentStatusAsync(cancellationToken);
            return ToolResult.Success("Henüz kodlama işi yok. Claude Code: " + status.Message);
        }

        var sb = new StringBuilder();
        foreach (var j in jobs)
        {
            sb.AppendLine($"#{j.Id} {j.Title} · {Label(j.Status)} · {j.Folder}");
            if (j.Progress is { } p && j.Status == CodingStatus.Calisiyor)
                sb.AppendLine("  şu an: " + p);
            if (j.LastMessage is { Length: > 0 } m)
                sb.AppendLine("  son mesaj (VERİ): " + (m.Length > 1200 ? m[..1200] + "…" : m));
        }

        return ToolResult.Success(sb.ToString().TrimEnd());
    }

    internal static string Label(CodingStatus status) => status switch
    {
        CodingStatus.Calisiyor => "çalışıyor",
        CodingStatus.Soruyor => "SORU SORDU, cevap bekliyor",
        CodingStatus.Bitti => "bitti",
        CodingStatus.Hata => "hata",
        CodingStatus.Bekliyor => "kullanım limiti doldu, bekliyor",
        _ => "iptal",
    };
}

/// <summary>Claude'un sorusuna cevap ya da "devam et" (aynı oturum).</summary>
public sealed class CodingReplyTool(CodingService coding) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kodlama_cevapla",
        "Claude Code'un sorduğu soruya kullanıcının cevabını iletir ya da yarım kalan (limit dolan) kodlama işini devam ettirir. Ek istek de verilebilir (ör. 'karanlık tema da ekle').",
        """{"type":"object","properties":{"cevap":{"type":"string","description":"Kullanıcının cevabı ya da ek isteği; 'devam et' de olur"}},"required":["cevap"]}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var job = await coding.ReplyAsync(ToolArgs.String(arguments, "cevap") ?? "devam et", cancellationToken: cancellationToken);
            return ToolResult.Success($"Cevabın #{job.Id} işine iletildi; Claude Code devam ediyor.");
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }
}

public sealed class CodingCancelTool(CodingService coding) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kodlama_iptal",
        "Çalışan kodlama işini durdurur (o ana kadar yazılan dosyalar kalır).",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(coding.Cancel() ? ToolResult.Success("Kodlama işi durduruluyor.") : ToolResult.Fail("Çalışan bir kodlama işi yok."));
}
