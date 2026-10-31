using System.Text;
using System.Text.Json;
using Ultron.Core.Tools;

namespace Ultron.Core.Sites;

/// <summary>"Okulumun sitesini takip et: https://…" → site eklenir ve hemen taranır.</summary>
public sealed class AddSiteTool(SiteService sites) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "site_ekle",
        "Bir web sitesini Ultron'un takip ettiği sitelere ekler: site her gece taranır (PDF'ler dahil) ve sorular internete çıkmadan sitenin kopyasından cevaplanır.",
        """
        {"type":"object","properties":{
          "adres":{"type":"string","description":"Sitenin adresi (ör. https://www.okul.edu.tr)"},
          "ad":{"type":"string","description":"Kısa ad (ör. Okulum)"},
          "amac":{"type":"string","description":"Ne için takip edileceği (ör. akademik takvim, duyurular, günlük burç yorumu)"}
        },"required":["adres"]}
        """);

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgs.String(arguments, "adres") is not { } url)
            return ToolResult.Fail("Sitenin adresi gerekli.");
        var (site, message) = await sites.AddAsync(url, ToolArgs.String(arguments, "ad"), ToolArgs.String(arguments, "amac"), cancellationToken);
        return site is null ? ToolResult.Fail(message) : ToolResult.Success(message);
    }
}

public sealed class ListSitesTool(SiteService sites) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "siteleri_listele",
        "Ultron'un takip ettiği siteleri (numara, ad, sayfa sayısı, son tarama) gösterir.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var list = await sites.ListAsync(cancellationToken);
        if (list.Count == 0)
            return ToolResult.Success("Takip edilen site yok.");
        var sb = new StringBuilder("Takip edilen siteler:\n");
        foreach (var s in list)
            sb.AppendLine($"#{s.Id} {s.Name} — {s.Url} · {s.PageCount} sayfa · son tarama: {(s.LastCrawledUtc is { } t ? t.ToLocalTime().ToString("dd.MM HH:mm") : "henüz yok")}{(string.IsNullOrEmpty(s.Purpose) ? "" : " · amaç: " + s.Purpose)}");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

public sealed class RemoveSiteTool(SiteService sites) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "site_sil",
        "Takip edilen bir siteyi (numarasıyla) ve Ultron'daki kopyasını siler.",
        """{"type":"object","properties":{"numara":{"type":"integer"}},"required":["numara"]}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        ToolArgs.Long(arguments, "numara") is { } id && await sites.RemoveAsync(id, cancellationToken)
            ? ToolResult.Success($"#{id} numaralı site ve kopyası silindi.")
            : ToolResult.Fail("Bu numarada takip edilen site yok.");
}

public sealed class CrawlSiteTool(SiteService sites) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "site_tara",
        "Takip edilen siteyi (numarası verilmezse hepsini) şimdi yeniden tarar; arka planda çalışır.",
        """{"type":"object","properties":{"numara":{"type":"integer"}}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var list = await sites.ListAsync(cancellationToken);
        var targets = ToolArgs.Long(arguments, "numara") is { } id ? list.Where(s => s.Id == id).ToList() : list.ToList();
        if (targets.Count == 0)
            return ToolResult.Fail("Taranacak site yok.");
        foreach (var s in targets)
            sites.RequestCrawl(s.Id);
        return ToolResult.Success($"{string.Join(", ", targets.Select(s => s.Name))} taranıyor (arka planda, birkaç dakika).");
    }
}
