using System.Text;
using System.Text.Json;
using Ultron.Core.Tools;

namespace Ultron.Core.Computer;

/// <summary>"Geçen hafta indirdiğim PDF'ler", "masaüstünde termo geçen dosyalar".</summary>
public sealed class FindFilesTool(FileOrganizer files) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "dosya_bul",
        "İndirilenler, Masaüstü ve Belgeler'de (alt klasörler dahil) dosya arar: ad parçası, tür (pdf, resim, video, word, excel, sunum, arşiv, kurulum, kod), son kaç gün.",
        """{"type":"object","properties":{"ad":{"type":"string"},"tur":{"type":"string"},"son_gun":{"type":"integer"},"klasor":{"type":"string","description":"indirilenler, masaüstü, belgeler (boşsa hepsi)"}}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var found = files.Find(ToolArgs.String(arguments, "ad"), ToolArgs.String(arguments, "tur"), (int?)ToolArgs.Long(arguments, "son_gun"), ToolArgs.String(arguments, "klasor"));
        if (found.Count == 0)
            return Task.FromResult(ToolResult.Success("Uyan dosya bulunamadı."));
        var sb = new StringBuilder($"{found.Count} dosya (en yeni önce):\n");
        foreach (var f in found)
            sb.AppendLine($"- {f.FullName} · {f.LastWriteTime:dd.MM.yyyy HH:mm} · {Size(f.Length)}");
        return Task.FromResult(ToolResult.Success(sb.ToString().TrimEnd()));
    }

    internal static string Size(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";
}

/// <summary>"Bu dosyayı Belgeler'e taşı", "rapor.pdf'in adını termo-rapor.pdf yap" (geri alınabilir).</summary>
public sealed class MoveFileTool(FileOrganizer files) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "dosya_tasi",
        "Bir dosyayı ya da klasörü başka klasöre taşır veya adını değiştirir (hedef sadece yeni ad ise yeniden adlandırır). Geri alınabilir.",
        """{"type":"object","properties":{"kaynak":{"type":"string","description":"Tam yol"},"hedef":{"type":"string","description":"Hedef klasör (ör. belgeler, C:\\...\\Termo) ya da yeni ad"}},"required":["kaynak","hedef"]}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(Target(arguments) is not { } t || PathGuard.IsSystem(t.From) || PathGuard.IsSystem(t.To) ? PermissionLevel.Yasak
            : files.IsAllowed(t.From) && files.IsAllowed(t.To) ? PermissionLevel.Bildirerek
            : PermissionLevel.Onayli);

    public string Describe(JsonElement arguments) =>
        Target(arguments) is { } t ? $"Taşı: {t.From} → {t.To}" : "Dosya taşı";

    private static FileMove? Target(JsonElement arguments)
    {
        if (PathGuard.Resolve(ToolArgs.String(arguments, "kaynak")) is not { } from || ToolArgs.String(arguments, "hedef") is not { } target)
            return null;
        // Sadece ad ("yeni-ad.pdf"): aynı klasörde yeniden adlandır. Klasör ("belgeler", tam yol): içine taşı.
        if (target.IndexOfAny(['\\', '/', ':']) < 0 && FileOrganizer.ResolveFolder(target) is { } known && FileOrganizer.Roots.Contains(known))
            return new FileMove(from, Path.Combine(known, Path.GetFileName(from)));
        if (target.IndexOfAny(['\\', '/', ':']) < 0)
            return new FileMove(from, Path.Combine(Path.GetDirectoryName(from)!, target + (Path.HasExtension(target) ? "" : Path.GetExtension(from))));
        return PathGuard.Resolve(target) is { } to ? new FileMove(from, Directory.Exists(to) ? Path.Combine(to, Path.GetFileName(from)) : to) : null;
    }

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (Target(arguments) is not { } move)
            return Task.FromResult(ToolResult.Fail("Kaynak ve hedef anlaşılamadı."));
        if (!File.Exists(move.From) && !Directory.Exists(move.From))
            return Task.FromResult(ToolResult.Fail($"{move.From} bulunamadı."));
        var batch = files.Apply($"Taşı: {Path.GetFileName(move.From)}", [move]);
        return Task.FromResult(batch.Moves.Count == 1
            ? ToolResult.Success($"{move.From} → {batch.Moves[0].To}. Geri almak için \"geri al\" demen yeterli.")
            : ToolResult.Fail("Taşınamadı."));
    }
}

/// <summary>"İndirilenler'i düzenle": önce plan onay kartında, onaylanınca yapılır.</summary>
public sealed class OrganizeFolderTool(FileOrganizer files) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "klasor_duzenle",
        "Bir klasördeki (indirilenler, masaüstü, belgeler) dosyaları türüne (Belgeler, Resimler, Videolar…) ya da ayına göre alt klasörlere toplar. Önce plan gösterilir, onayla yapılır; geri alınabilir.",
        """{"type":"object","properties":{"klasor":{"type":"string"},"kural":{"type":"string","enum":["tur","tarih"]}},"required":["klasor"]}""");

    public PermissionLevel Level => PermissionLevel.Onayli;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(Folder(arguments) is { } f && files.IsAllowed(f) ? PermissionLevel.Onayli : PermissionLevel.Yasak);

    public string Describe(JsonElement arguments) =>
        Folder(arguments) is { } f ? "Klasörü düzenle — " + FileOrganizer.Summary(f, files.Plan(f, Rule(arguments))) : "Klasörü düzenle";

    private static string? Folder(JsonElement arguments) => FileOrganizer.ResolveFolder(ToolArgs.String(arguments, "klasor"));

    private static string Rule(JsonElement arguments) => ToolArgs.String(arguments, "kural") ?? "tur";

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (Folder(arguments) is not { } folder || !files.IsAllowed(folder))
            return Task.FromResult(ToolResult.Fail("Sadece İndirilenler, Masaüstü ve Belgeler düzenlenebilir."));
        var plan = files.Plan(folder, Rule(arguments));
        if (plan.Count == 0)
            return Task.FromResult(ToolResult.Success(FileOrganizer.Summary(folder, plan)));
        var batch = files.Apply($"Düzenle: {Path.GetFileName(folder)}", plan);
        return Task.FromResult(ToolResult.Success(FileOrganizer.Summary(folder, batch.Moves) + ". Beğenmezsen \"geri al\" de, hepsi eski yerine döner."));
    }
}

public sealed class UndoFilesTool(FileOrganizer files) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "dosya_islemini_geri_al",
        "Son dosya taşıma/düzenleme işini geri alır (dosyalar eski yerlerine döner).",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var (batch, restored, skipped) = files.UndoLast();
        return Task.FromResult(batch is null
            ? ToolResult.Success("Geri alınacak dosya işi yok.")
            : ToolResult.Success($"\"{batch.Title}\" geri alındı: {restored} dosya eski yerine döndü{(skipped > 0 ? $", {skipped} dosya (yeri dolu ya da silinmiş) olduğu yerde kaldı" : "")}."));
    }
}

/// <summary>"Bu üç PDF'i birleştir", "rapor.pdf'in 3-5. sayfalarını ayrı dosya yap".</summary>
public sealed class PdfTool(FileOrganizer files, IPdfTools pdf) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "pdf",
        "PDF birleştirir (birlestir: dosyalar sırayla tek PDF olur) ya da böler (bol: seçilen sayfalar yeni PDF olur, ör. sayfalar=\"1-3,5\"). Asıl dosyalar değişmez.",
        """
        {"type":"object","properties":{
          "islem":{"type":"string","enum":["birlestir","bol"]},
          "dosyalar":{"type":"array","items":{"type":"string"},"description":"Tam yollar (birlestir için 2+, bol için 1)"},
          "sayfalar":{"type":"string"},
          "cikti":{"type":"string","description":"Yeni dosyanın adı (isteğe bağlı)"}
        },"required":["islem","dosyalar"]}
        """);

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(Inputs(arguments).All(files.IsAllowed) ? PermissionLevel.Bildirerek : PermissionLevel.Onayli);

    private static List<string> Inputs(JsonElement arguments)
    {
        arguments = ToolArgs.Normalize(arguments);
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("dosyalar", out var list))
            return [];
        var items = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(e => e.GetString()) : [list.GetString()];
        return items.Select(PathGuard.Resolve).OfType<string>().ToList();
    }

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var inputs = Inputs(arguments);
        if (inputs.Count == 0 || inputs.Any(i => !File.Exists(i) || !i.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(ToolResult.Fail("PDF dosyaları bulunamadı: " + string.Join(", ", inputs.Where(i => !File.Exists(i)))));
        var dir = Path.GetDirectoryName(inputs[0])!;
        var name = ToolArgs.String(arguments, "cikti");
        try
        {
            if (ToolArgs.String(arguments, "islem") == "bol")
            {
                var pages = FileOrganizer.ParsePages(ToolArgs.String(arguments, "sayfalar") ?? "", pdf.PageCount(inputs[0]));
                if (pages.Count == 0)
                    return Task.FromResult(ToolResult.Fail("Geçerli sayfa yok (ör. sayfalar=\"1-3,5\")."));
                var output = FileOrganizer.Unique(Path.Combine(dir, Pdfify(name ?? $"{Path.GetFileNameWithoutExtension(inputs[0])} (s. {ToolArgs.String(arguments, "sayfalar")})")));
                var count = pdf.Split(inputs[0], pages, output);
                return Task.FromResult(ToolResult.Success($"{count} sayfalık yeni PDF: {output}"));
            }
            if (inputs.Count < 2)
                return Task.FromResult(ToolResult.Fail("Birleştirmek için en az 2 PDF gerekli."));
            var merged = FileOrganizer.Unique(Path.Combine(dir, Pdfify(name ?? "Birleştirilmiş")));
            var total = pdf.Merge(inputs, merged);
            return Task.FromResult(ToolResult.Success($"{inputs.Count} PDF birleştirildi ({total} sayfa): {merged}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ToolResult.Fail("PDF işlenemedi (şifreli ya da bozuk olabilir): " + ex.Message));
        }
    }

    private static string Pdfify(string name) => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
}
