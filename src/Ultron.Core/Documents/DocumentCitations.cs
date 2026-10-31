using System.Text.RegularExpressions;
using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Core.Documents;

/// <summary>
/// Cevaptaki [n] numaralarını kontrol eder. Model çoğu zaman en kolay numarayı ([1]) yazıyor; [1] kapak sayfasıysa
/// kaynak kartı yanlış sayfayı gösterir (canlı deneme: Rankine bilgisi s. 5'te, [1] = kapak). Her cümle, kelimeleri
/// en çok örtüşen parçaya bağlanır: öğretmenin "bu cümleyi hangi sayfadan aldın?" diye kontrol etmesi gibi.
/// </summary>
public static partial class DocumentCitations
{
    /// <summary>Daha iyi bir parça en az bu kadar ortak kelime köküyle öndeyse numara değiştirilir.</summary>
    private const int MinAdvantage = 2;

    public static string Repair(string answer, IReadOnlyList<DocumentSource> sources)
    {
        answer = SplitLists(answer);
        if (sources.Count < 2 || !Marker().IsMatch(answer))
            return answer;

        var roots = sources.ToDictionary(s => s.Number, s => Roots(s.Text));
        return Sentence().Replace(answer, sentence =>
        {
            var markers = Marker().Matches(sentence.Value);
            if (markers.Count == 0)
                return sentence.Value;

            var words = KeywordQuery.Prefixes(Marker().Replace(sentence.Value, " "));
            int Overlap(int n) => roots.TryGetValue(n, out var r) ? words.Count(r.Contains) : -1;
            var best = sources.Select(s => s.Number).MaxBy(Overlap);
            return Marker().Replace(sentence.Value, m =>
            {
                var cited = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return Overlap(best) - Overlap(cited) >= MinAdvantage ? $"[{best}]" : m.Value;
            });
        });
    }

    /// <summary>"[1, 2]" ya da "[1,2]" → "[1][2]": her numara ayrı kontrol edilsin ve kartta görünsün.</summary>
    public static string SplitLists(string answer) =>
        MarkerList().Replace(answer, m => string.Concat(m.Groups[1].Value.Split(',').Select(n => $"[{n.Trim()}]")));

    /// <summary>
    /// Kaynak kartı: cevapta [n] ile anılan parçalar. Model numara yazmayı unuttuysa (değerlendirme: "Ara sınav 14 Kasım'da" doğru
    /// ama numarasız) cevapla kelimeleri açıkça en çok örtüşen tek parça. "Notlarda bulamadım" cevabının altında kart olmaz.
    /// </summary>
    public static IReadOnlyList<DocumentSource> Cited(IReadOnlyList<DocumentSource> sources, string answer)
    {
        var numbers = Marker().Matches(SplitLists(answer)).Select(m => m.Groups[1].Value).ToHashSet();
        if (numbers.Count > 0)
            return sources.Where(s => numbers.Contains(s.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        if (sources.Count == 0 || NotFound().IsMatch(" " + TurkishText.Normalize(answer) + " "))
            return [];

        var words = KeywordQuery.Prefixes(answer);
        var ranked = sources.Select(s => (Source: s, Overlap: words.Count(Roots(s.Text).Contains))).OrderByDescending(x => x.Overlap).ToList();
        var runnerUp = ranked.Count > 1 ? ranked[1].Overlap : 0;
        return ranked[0].Overlap >= MinInferredOverlap && ranked[0].Overlap - runnerUp >= MinAdvantage ? [ranked[0].Source] : [];
    }

    /// <summary>Numarasız cevapta kart tahmini için en az bu kadar ortak kelime kökü.</summary>
    private const int MinInferredOverlap = 3;

    private static HashSet<string> Roots(string text) =>
        TurkishText.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= KeywordQuery.PrefixLength)
            .Select(w => w[..KeywordQuery.PrefixLength])
            .ToHashSet();

    [GeneratedRegex(@"\[(\d{1,2})\]")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\[(\d{1,2}(?:\s*,\s*\d{1,2})+)\]")]
    private static partial Regex MarkerList();

    // Normalleştirilmiş metinde "bulamadım / bulunmuyor / geçmiyor / yer almıyor / bahsedilmiyor / yok".
    [GeneratedRegex(@" (bulamad|bulunmuyor|bulunamad|gecmiyor|yer almiyor|bahsedilmiyor|bahsetmiyor|yok )")]
    private static partial Regex NotFound();

    // Cümle: sonu . ! ? … (ve ardından gelen [n] işaretleri) ya da satır sonu.
    [GeneratedRegex(@"[^.!?…\n]+(?:[.!?…]+(?:\s*\[\d{1,2}\])*|$)", RegexOptions.Multiline)]
    private static partial Regex Sentence();
}
