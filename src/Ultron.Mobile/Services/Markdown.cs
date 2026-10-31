using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Ultron.Mobile.Services;

/// <summary>Cevaplardaki basit markdown (kalın, kod, liste, başlık) → güvenli HTML. Önce her şey kaçışlanır, sonra işaretler çevrilir.</summary>
public static partial class Markdown
{
    public static MarkupString Html(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new MarkupString("");
        var html = new StringBuilder();
        var parts = text.Split("```");
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                var code = parts[i];
                var newline = code.IndexOf('\n');
                if (newline >= 0 && newline < 20 && !code[..newline].Contains(' '))
                    code = code[(newline + 1)..]; // dil adı (```csharp)
                html.Append("<pre>").Append(WebUtility.HtmlEncode(code.TrimEnd())).Append("</pre>");
                continue;
            }
            foreach (var paragraph in Regex.Split(parts[i].Trim(), @"\n\s*\n"))
            {
                if (paragraph.Length == 0)
                    continue;
                var lines = paragraph.Split('\n').Select(l => Inline(WebUtility.HtmlEncode(l.Trim())));
                html.Append("<p>").Append(string.Join("<br>", lines)).Append("</p>");
            }
        }
        return new MarkupString(html.ToString());
    }

    private static string Inline(string line)
    {
        line = Heading().Replace(line, "<b>$1</b>");
        line = Bullet().Replace(line, "• ");
        line = Bold().Replace(line, "<b>$1</b>");
        return Code().Replace(line, "<code>$1</code>");
    }

    [GeneratedRegex(@"^#{1,6}\s*(.+)$")] private static partial Regex Heading();
    [GeneratedRegex(@"^[-*]\s+")] private static partial Regex Bullet();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
}
