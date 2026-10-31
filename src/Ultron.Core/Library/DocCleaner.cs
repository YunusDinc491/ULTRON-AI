using System.Text;
using System.Text.RegularExpressions;
using Ultron.Core.Documents;

namespace Ultron.Core.Library;

/// <summary>
/// Doküman dosyasını (Markdown, MDX, MDN makroları, AsciiDoc) aranabilir düz metne çevirir. Kod blokları olduğu gibi kalır
/// (yazılım sorularında asıl değerli kısım). Kitabın kapağını, dipnot işaretlerini ve sayfa numaralarını atıp metni bırakmak gibi.
/// </summary>
public static partial class DocCleaner
{
    public sealed record CleanPage(string Title, string Text, IReadOnlyList<(string Heading, string Body)> Sections);

    public static CleanPage Clean(string raw, string fileName)
    {
        var text = raw.Replace("\r\n", "\n");
        string? title = null;

        // Ön bilgi (frontmatter): "---\ntitle: Array.prototype.map()\n---"
        if (text.StartsWith("---\n", StringComparison.Ordinal) && text.IndexOf("\n---", 4, StringComparison.Ordinal) is > 0 and var end)
        {
            var front = text[4..end];
            title = FrontTitle().Match(front) is { Success: true } m ? m.Groups[1].Value.Trim().Trim('"', '\'') : null;
            text = text[(end + 4)..];
        }

        text = LatestMoniker(text);
        var output = new StringBuilder();
        var inCode = false;
        foreach (var original in text.Split('\n'))
        {
            var line = original;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal) || trimmed == "----")
            {
                inCode = !inCode;
                output.AppendLine(trimmed == "----" ? "```" : trimmed);
                continue;
            }
            if (inCode)
            {
                output.AppendLine(line);
                continue;
            }
            if (MdxStatement().IsMatch(line) || AdocAttribute().IsMatch(line) || DocfxDirective().IsMatch(line))
                continue;
            line = DocfxCallout().Replace(line, m => "> " + CalloutName(m.Groups[1].Value) + ":");
            output.AppendLine(CleanLine(line));
        }

        var body = Comments().Replace(output.ToString(), "");
        body = BlankLines().Replace(body, "\n\n").Trim();

        // AsciiDoc başlıkları ("== Başlık") Markdown'a çevrilir; ilk "# " başlık yoksa başlık olur.
        body = AdocHeading().Replace(body, m => new string('#', m.Groups[1].Length) + " " + m.Groups[2].Value);
        title ??= FirstHeading().Match(body) is { Success: true } h ? h.Groups[1].Value.Trim() : TitleFromFile(fileName);

        return new CleanPage(title, body, Sections(body));
    }

    /// <summary>Satır içi temizlik (kod bloğu dışında).</summary>
    internal static string CleanLine(string line)
    {
        // Linkler önce satırın tamamında ("[`map()`](/docs/…)" → "`map()`"); sonra satır içi kod (`<div>`, `{{ }}`) korunarak
        // sadece kod dışındaki kısımlar temizlenir.
        line = Image().Replace(line, "");
        line = Link().Replace(line, "$1");
        line = Xref().Replace(line, "$1");
        var parts = InlineCode().Split(line);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith('`'))
                continue;
            var p = Macro().Replace(parts[i], MacroText);
            p = AdocLink().Replace(p, "$1");
            parts[i] = Tag().Replace(p, "");
        }
        return string.Concat(parts).TrimEnd();
    }

    [GeneratedRegex(@"(`+[^`]*`+)")]
    private static partial Regex InlineCode();

    /// <summary>MDN makrosu: {{jsxref("Array/map", "map()")}} → map(); {{Glossary("CORS")}} → CORS; {{Compat}} → (silinir).</summary>
    private static string MacroText(Match m)
    {
        var args = MacroArgs().Matches(m.Groups[2].Value).Select(a => a.Groups[1].Value).ToList();
        if (args.Count == 0)
            return "";
        var shown = args.Count >= 2 && args[1].Length > 0 ? args[1] : args[0];
        return shown.Contains('/') ? shown[(shown.LastIndexOf('/') + 1)..] : shown;
    }

    /// <summary>
    /// Microsoft Learn sayfaları aynı konuyu sürüm sürüm tekrarlar (":::moniker range=\">= aspnetcore-9.0\"" … ":::moniker-end").
    /// Hepsi alınırsa kütüphane şişer ve eski sürümün bilgisi gelebilir: sayfada geçen en yeni sürümü kapsayan bloklar kalır, gerisi atılır.
    /// </summary>
    internal static string LatestMoniker(string text)
    {
        var blocks = MonikerBlock().Matches(text);
        if (blocks.Count == 0)
            return text;
        var latest = blocks.SelectMany(b => MonikerVersion().Matches(b.Groups[1].Value)).Max(v => Version.Parse(v.Groups[2].Value));
        if (latest is null)
            return text;
        return MonikerBlock().Replace(text, b => Includes(b.Groups[1].Value, latest) ? b.Groups[2].Value : "");
    }

    /// <summary>"&gt;= aspnetcore-6.0 &lt; aspnetcore-8.0" gibi aralık <paramref name="version"/>'ı kapsıyor mu (her koşul sağlanmalı; sade "aspnetcore-8.0" = eşit).</summary>
    private static bool Includes(string range, Version version) =>
        MonikerVersion().Matches(range).All(c =>
        {
            var v = Version.Parse(c.Groups[2].Value);
            return c.Groups[1].Value switch
            {
                ">=" => version >= v,
                ">" => version > v,
                "<=" => version <= v,
                "<" => version < v,
                _ => version == v,
            };
        });

    private static string CalloutName(string kind) => kind.ToUpperInvariant() switch
    {
        "WARNING" or "CAUTION" => "Uyarı",
        "IMPORTANT" => "Önemli",
        "TIP" => "İpucu",
        _ => "Not",
    };

    /// <summary>Başlıklara göre bölümler: her parça "Sayfa › Bölüm" bağlamıyla aranır.</summary>
    private static IReadOnlyList<(string Heading, string Body)> Sections(string body)
    {
        var result = new List<(string, string)>();
        var heading = "";
        var current = new StringBuilder();
        var inCode = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inCode = !inCode;
            if (!inCode && SectionHeading().Match(line) is { Success: true } h)
            {
                Flush();
                heading = h.Groups[1].Value.Trim();
                continue;
            }
            current.AppendLine(line);
        }
        Flush();
        return result;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0)
                result.Add((heading, text));
            current.Clear();
        }
    }

    /// <summary>
    /// Sayfayı fişlere böler: bölüm sınırlarında, her fişin başında "Sayfa › Bölüm" bağlamı. Kod bloğu parçanın ortasında kesilmez
    /// (bölünmesi gerekirse satır sınırında).
    /// </summary>
    public static IReadOnlyList<string> Chunk(CleanPage page, int size = TextChunker.DefaultSize, int overlap = TextChunker.DefaultOverlap)
    {
        var chunks = new List<string>();
        foreach (var (heading, body) in page.Sections)
        {
            var context = heading.Length == 0 ? page.Title : $"{page.Title} › {heading}";
            foreach (var part in TextChunker.SplitText(body, size, overlap))
                chunks.Add(context + "\n" + part);
        }
        return chunks;
    }

    private static string TitleFromFile(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name is "index" or "README" or "_index")
            name = Path.GetFileName(Path.GetDirectoryName(fileName) ?? name);
        return name.Replace('-', ' ').Replace('_', ' ');
    }

    [GeneratedRegex(@"^title:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex FrontTitle();

    [GeneratedRegex(@"^\s*(import\s.+from\s|import\s+['""]|export\s+(const|default|function|\{))")]
    private static partial Regex MdxStatement();

    [GeneratedRegex(@"^:[\w-]+:")]
    private static partial Regex AdocAttribute();

    [GeneratedRegex(@"^(={1,5})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex AdocHeading();

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex FirstHeading();

    [GeneratedRegex(@"^#{2,4}\s+(.+)$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][\w-]*)\s*(\([^}]*\))?\s*\}\}")]
    private static partial Regex Macro();

    [GeneratedRegex(@"[""']([^""']*)[""']")]
    private static partial Regex MacroArgs();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?:linkgit|link|xref):([\w./-]+)\[[^\]]*\]")]
    private static partial Regex AdocLink();

    [GeneratedRegex(@"</?[A-Za-z][A-Za-z0-9.]*(\s[^<>]*)?/?>")]
    private static partial Regex Tag();

    /// <summary>DocFX: ":::code …:::", ":::zone pivot=…", ":::image …", "[!INCLUDE[…](…)]", "[!code-csharp[](…)]", "> [!div class=…]".</summary>
    [GeneratedRegex(@"^\s*(:::|>?\s*\[!(INCLUDE|include|code-|div|VIDEO|video))")]
    private static partial Regex DocfxDirective();

    [GeneratedRegex(@"^\s*>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DocfxCallout();

    /// <summary>"&lt;xref:System.String&gt;" → System.String (isteğe bağlı ?displayProperty=… atılır).</summary>
    [GeneratedRegex(@"<xref:([^>?#\s]+)[^>]*>")]
    private static partial Regex Xref();

    [GeneratedRegex(@"^[ \t]*:::moniker\s+range=""([^""]+)""[ \t]*\n(.*?)^[ \t]*:::moniker-end[ \t]*$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex MonikerBlock();

    [GeneratedRegex(@"(>=|<=|>|<)?\s*[a-z]+(?:-[a-z]+)*-(\d+(?:\.\d+)+)")]
    private static partial Regex MonikerVersion();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
