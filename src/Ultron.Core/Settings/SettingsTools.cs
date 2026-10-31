using System.Text;
using System.Text.Json;
using Ultron.Core.Tools;

namespace Ultron.Core.Settings;

public sealed class ShowSettingsTool(UltronSettings settings) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ayarlari_goster",
        "Ultron'un kullanıcı tarafından değiştirilebilen ayarlarını (brifing saati/günleri, sessiz saatler, varsayılan şehir, günlük öneri sayısı, sabah uyandırma, ikinci yedek yeri, Ultron deyince dinleme, formül ve yazı boyutu) gösterir.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("Ayarlar:\n");
        foreach (var (d, value) in await settings.GetAllAsync(cancellationToken))
            sb.AppendLine($"{d.Name} ({d.Key}): {value}");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

/// <summary>"Brifingi 8'e al", "sessiz saatler gece 12'de başlasın", "varsayılan şehrim Ankara".</summary>
public sealed class ChangeSettingTool(UltronSettings settings) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ayar_degistir",
        "Ultron'un bir ayarını değiştirir: brifing saati (SS:DD), brifing günleri (hafta_ici/her_gun/hafta_sonu/kapali), " +
        "sessiz saatlerin başlangıcı ve bitişi (SS:DD), varsayılan şehir, günlük öneri sınırı (sinirsiz, 1-10 ya da 0 = önerileri kapat), sabah uyandırma (acik/kapali), ikinci yedek yeri (tam klasör yolu ya da yok), \"Ultron\" deyince dinleme (wake_word: acik/kapali), hitap (Ultron'un kullanıcıya nasıl seslendiği; yok = isimle hitap yok), ekrandaki formül ve yazı boyutu (formul_boyutu 70-220, yazi_boyutu 80-160, yüzde; ya da buyut/kucult/varsayilan).",
        """
        {"type":"object","properties":{
          "ayar":{"type":"string","enum":["brifing_saati","brifing_gunleri","sessiz_baslangic","sessiz_bitis","varsayilan_sehir","oneri_sayisi","uyandirma","yedek_ikinci_yer","wake_word","formul_boyutu","yazi_boyutu"]},
          "deger":{"type":"string","description":"Yeni değer (ör. 08:00, hafta_ici, Ankara)"}
        },"required":["ayar","deger"]}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var key = ToolArgs.String(arguments, "ayar");
        var value = ToolArgs.String(arguments, "deger");
        if (key is null || value is null)
            return ToolResult.Fail("'ayar' ve 'deger' gerekli.");
        if (key is UltronSettings.FormulaSize or UltronSettings.TextSize)
            value = await RelativeSizeAsync(key, value, cancellationToken);
        if (await settings.SetAsync(key, value, cancellationToken) is { } error)
            return ToolResult.Fail(error);
        var definition = UltronSettings.Definitions.First(d => d.Key == key);
        return ToolResult.Success($"{definition.Name} artık {await settings.GetAsync(key, cancellationToken)}.");
    }

    /// <summary>"buyut" / "kucult" / "varsayilan": şu anki boyuta göre bir adım (sınırda kalır).</summary>
    private async Task<string> RelativeSizeAsync(string key, string value, CancellationToken cancellationToken)
    {
        var word = Text.TurkishText.Normalize(value);
        var step = word.StartsWith("buyu", StringComparison.Ordinal) || word.StartsWith("artir", StringComparison.Ordinal) ? 1
            : word.StartsWith("kucu", StringComparison.Ordinal) || word.StartsWith("azalt", StringComparison.Ordinal) ? -1 : 0;
        if (word is "varsayilan" or "normal" or "sifirla" or "eski")
            return UltronSettings.Definitions.First(d => d.Key == key).Default;
        if (step == 0)
            return value;
        var (min, max) = UltronSettings.SizeRange(key);
        var current = int.Parse(await settings.GetAsync(key, cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return Math.Clamp(current + step * UltronSettings.SizeStep, min, max).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
