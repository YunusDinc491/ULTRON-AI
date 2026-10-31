using System.Text;
using System.Text.RegularExpressions;

namespace Ultron.Core.Documents;

/// <summary>
/// Sayfaları aranabilir parçalara böler. Kitabı fişlere ayırmak gibi: her fiş tek başına anlaşılır
/// olsun diye paragraf/cümle sınırında kesilir, sonraki fiş öncekinin son cümlesini tekrar eder (örtüşme).
/// Parça sayfa sınırını aşmaz: kaynak kartı her zaman tek bir sayfa gösterir.
/// </summary>
public static partial class TextChunker
{
    public const int DefaultSize = 900;
    public const int DefaultOverlap = 150;

    public static IReadOnlyList<(int? Page, string Text)> Split(IEnumerable<DocumentPage> pages, int size = DefaultSize, int overlap = DefaultOverlap)
    {
        var chunks = new List<(int?, string)>();
        foreach (var page in pages)
        {
            foreach (var text in SplitText(Clean(page.Text), size, overlap))
                chunks.Add((page.Page, text));
        }

        return chunks;
    }

    /// <summary>PDF'ten gelen fazla boşlukları ve satır sonu tirelerini toplar ("ener-\nji" → "enerji").</summary>
    public static string Clean(string text)
    {
        text = text.Replace("\r\n", "\n").Replace(' ', ' ');
        text = HyphenBreak().Replace(text, "$1$2");
        text = Spaces().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    internal static IEnumerable<string> SplitText(string text, int size, int overlap)
    {
        if (text.Length == 0)
            yield break;
        if (text.Length <= size)
        {
            yield return text;
            yield break;
        }

        // Önce cümlelere ayır (çok uzun "cümleler" kelime sınırında bölünür), sonra cümleleri parçaya doldur.
        var sentences = Sentences(text).SelectMany(s => s.Length <= size ? [s] : Words(s, size)).ToList();
        var current = new StringBuilder();
        var start = 0;
        for (var i = 0; i < sentences.Count; i++)
        {
            if (current.Length > 0 && current.Length + 1 + sentences[i].Length > size)
            {
                yield return current.ToString().Trim();

                // Örtüşme: son cümlelerden overlap karakter kadarını yeni parçanın başına al (ama ilerlemeyi garanti et).
                var back = i;
                var carried = 0;
                while (back - 1 > start && carried + sentences[back - 1].Length <= overlap)
                    carried += sentences[--back].Length + 1;
                current.Clear();
                for (var j = back; j < i; j++)
                    current.Append(sentences[j]).Append(' ');
                start = back;
            }

            current.Append(sentences[i]).Append(' ');
        }

        if (current.ToString().Trim() is { Length: > 0 } last)
            yield return last;
    }

    private static IEnumerable<string> Sentences(string text) =>
        SentenceEnd().Split(text).Select(s => s.Replace('\n', ' ').Trim()).Where(s => s.Length > 0);

    private static IEnumerable<string> Words(string sentence, int size)
    {
        var sb = new StringBuilder();
        foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0 && sb.Length + 1 + word.Length > size)
            {
                yield return sb.ToString();
                sb.Clear();
            }

            sb.Append(sb.Length > 0 ? " " : "").Append(word.Length > size ? word[..size] : word);
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    [GeneratedRegex(@"(\p{L})-\n(\p{Ll})")]
    private static partial Regex HyphenBreak();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();

    // Cümle sonu: . ! ? (ardından boşluk) ya da paragraf boşluğu. "3.5" gibi sayılar bölünmez (ardında boşluk yok).
    [GeneratedRegex(@"(?<=[.!?…])\s+|\n\n")]
    private static partial Regex SentenceEnd();
}
