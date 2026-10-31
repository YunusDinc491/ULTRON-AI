using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ultron.Core.Studio;

/// <summary>Model cevabından bileşen kodunu ayırma ve tarif dosyalarını okuma.</summary>
public static partial class StudioCode
{
    /// <summary>
    /// Cevaptaki en uzun ```jsx/tsx/js/javascript bloğu kod, geri kalanı açıklama. Blok kapanmadıysa (cevap yarıda kesildi)
    /// açılıştan sonrası kod sayılır.
    /// </summary>
    public static (string? Code, string Explanation) Extract(string answer)
    {
        var blocks = Fence().Matches(answer).Where(m => m.Groups["lang"].Value is "" or "jsx" or "tsx" or "js" or "javascript" or "react" or "typescript" or "ts").ToList();
        if (blocks.Count > 0)
        {
            var best = blocks.OrderByDescending(m => m.Groups["code"].Length).First();
            var explanation = (answer[..best.Index] + answer[(best.Index + best.Length)..]).Trim();
            return (best.Groups["code"].Value.Trim(), Tidy(explanation));
        }

        var open = OpenFence().Match(answer);
        if (open.Success)
            return (answer[(open.Index + open.Length)..].Trim(), Tidy(answer[..open.Index]));
        return (null, Tidy(answer));
    }

    /// <summary>Önizlemenin çalıştırabileceği bileşen mi: varsayılan dışa aktarım var, React dışında import yok.</summary>
    public static IReadOnlyList<string> Problems(string code)
    {
        var problems = new List<string>();
        if (!code.Contains("export default", StringComparison.Ordinal))
            problems.Add("`export default function …` yok");
        foreach (Match m in ImportFrom().Matches(code))
            if (m.Groups["module"].Value != "react")
                problems.Add($"'{m.Groups["module"].Value}' içe aktarılıyor; önizlemede sadece 'react' var (ikonları satır içi SVG yap)");
        return problems;
    }

    /// <summary>Kodun ilk bileşen adı ("export default function Navbar" → "Navbar").</summary>
    public static string? ComponentName(string code) => DefaultName().Match(code) is { Success: true } m ? m.Groups["name"].Value : null;

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    /// <summary>Tarif dosyası: başta "---" arasında title/tags/description, altında ```jsx bloğu.</summary>
    public static StudioRecipe? ParseRecipe(string slug, string markdown)
    {
        var meta = FrontMatter().Match(markdown);
        if (!meta.Success)
            return null;
        string Field(string name) => Regex.Match(meta.Groups["body"].Value, $@"(?m)^{name}:\s*(.+)$") is { Success: true } f ? f.Groups[1].Value.Trim() : "";
        var (code, _) = Extract(markdown[(meta.Index + meta.Length)..]);
        if (string.IsNullOrWhiteSpace(code) || Field("title").Length == 0)
            return null;
        return new StudioRecipe
        {
            Slug = slug, Title = Field("title"), Tags = Field("tags"), Description = Field("description"), Code = code,
            Kind = RecipeKind.Hazir, ContentHash = Hash(markdown),
        };
    }

    /// <summary>Tarifin aranacak metni (anlam vektörü buna göre).</summary>
    public static string SearchText(StudioRecipe r) => $"{r.Title}. {r.Tags}. {r.Description}";

    private static string Tidy(string s) => Regex.Replace(s, @"\n{3,}", "\n\n").Trim();

    [GeneratedRegex(@"```(?<lang>[a-zA-Z]*)[^\n]*\n(?<code>.*?)\n?```", RegexOptions.Singleline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"```(?:jsx|tsx|js|javascript|react)?[^\n]*\n")]
    private static partial Regex OpenFence();

    [GeneratedRegex(@"(?m)^\s*import\s+[^;]*?from\s+['""](?<module>[^'""]+)['""]")]
    private static partial Regex ImportFrom();

    [GeneratedRegex(@"export\s+default\s+function\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex DefaultName();

    [GeneratedRegex(@"\A\s*---\s*\n(?<body>.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
}
