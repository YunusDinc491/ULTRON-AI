using System.Text;
using System.Text.RegularExpressions;

namespace Ultron.Desktop.Voice;

/// <summary>
/// Cevap kelime kelime akarken tamamlanan cümleleri ayırır: ilk cümle biter bitmez seslendirme başlar,
/// tüm cevabı beklemek gerekmez. Çok kısa parçalar (ör. "Evet.") bir sonrakiyle birleşir ki ses kesik kesik olmasın.
/// </summary>
public sealed class SentenceSplitter
{
    private readonly StringBuilder _buffer = new();

    public int MinLength { get; init; } = 25;

    /// <summary>
    /// En fazla bu kadar cümle okunur; ayrıntılı (uzun, maddeli) cevapların tamamını sesli okumak sıkıcı.
    /// Fazlası atlanır ve <see cref="Truncated"/> true olur (arayüz "gerisi ekranda" der).
    /// </summary>
    public int MaxSentences { get; init; } = int.MaxValue;

    public bool Truncated { get; private set; }

    /// <summary>
    /// Okuma bir listeye/başlığa/tabloya gelince durdu: liste maddelerini tek tek okumak dinleyene işkence;
    /// arayüz "ayrıntıları aşağıda listeledim" der.
    /// </summary>
    public bool ReachedList { get; private set; }

    /// <summary>Liste/başlık/tablo gelince okumayı durdur.</summary>
    public bool StopAtList { get; init; }

    /// <summary>
    /// Hızlı başlangıç (2026-10-27: ses geç başlıyordu): ilk cümle virgüllüyse ilk parça (en az <see cref="QuickMinLength"/> karakter)
    /// cümlenin bitmesi beklenmeden seslendirmeye gider. Bu parça cümle sayısına (<see cref="MaxSentences"/>) girmez.
    /// </summary>
    public bool QuickStart { get; init; }

    public int QuickMinLength { get; init; } = 14;

    private int _count;
    private bool _stopped;
    private bool _quickDone;

    // Satır başında madde işareti, numara, başlık ya da tablo: "- ", "* ", "• ", "1. ", "2) ", "## ", "| ".
    private static readonly Regex ListLine = new(@"(^|\n)[ \t]*([-*•+][ \t]|\d{1,2}[.)][ \t]|#{1,6}[ \t]|\|)", RegexOptions.Compiled);

    /// <summary>Yeni gelen parçayı ekler, tamamlanan cümleleri döndürür.</summary>
    public IReadOnlyList<string> Push(string chunk)
    {
        _buffer.Append(chunk);
        var result = new List<string>();
        if (_stopped)
        {
            Truncated |= _buffer.ToString().Trim().Length > 0;
            _buffer.Clear();
            return result;
        }
        var text = _buffer.ToString();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var quick = QuickStart && !_quickDone && _count == 0 && text[i] == ',' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])
                && i + 1 - start >= QuickMinLength;
            if (!quick && (!IsBoundary(text, i) || i + 1 - start < MinLength))
                continue;
            var raw = text[start..(i + 1)];
            start = i + 1;
            if (quick && !ListLine.IsMatch(raw))
            {
                _quickDone = true;
                result.Add(raw.Trim().TrimEnd(','));
                continue;
            }
            _quickDone = true;
            if (!Take(raw, result))
            {
                Truncated = true;
                start = text.Length;
                break;
            }
            if (_stopped)
            {
                Truncated |= text[start..].Trim().Length > 0;
                start = text.Length;
                break;
            }
        }

        _buffer.Remove(0, start);
        return result;
    }

    /// <summary>Akış bitti: kalan metni (yarım cümle de olsa) döndürür.</summary>
    public string? Flush()
    {
        var rest = _buffer.ToString();
        _buffer.Clear();
        if (rest.Trim().Length == 0)
            return null;
        if (_stopped)
        {
            Truncated = true;
            return null;
        }
        var taken = new List<string>(1);
        if (!Take(rest, taken))
            Truncated = true;
        return taken.Count > 0 ? taken[0] : null;
    }

    public void Reset()
    {
        _buffer.Clear();
        _count = 0;
        _stopped = false;
        _quickDone = false;
        Truncated = false;
        ReachedList = false;
    }

    /// <summary>
    /// Cümleyi okunacaklara ekler. Liste başladıysa listeden önceki kısmı alıp durur (false döner).
    /// </summary>
    private bool Take(string raw, List<string> result)
    {
        var list = StopAtList ? ListLine.Match(raw) : Match.Empty;
        if (list.Success)
        {
            ReachedList = true;
            _stopped = true;
            var before = raw[..list.Index].Trim();
            // "İşte ayrıntılar:" gibi listeye giriş cümlesi okunmaz; yerine "aşağıda listeledim" denir.
            if (before.Length > 0 && !before.EndsWith(':'))
            {
                result.Add(before);
                _count++;
            }
            return false;
        }
        var sentence = raw.Trim();
        if (sentence.Length > 0)
        {
            result.Add(sentence);
            if (++_count >= MaxSentences)
                _stopped = true;
        }
        return true;
    }

    // Cümle sonu: satır sonu ya da . ! ? … ; : ardından boşluk. "3.5" ve "vb.," gibi durumlar boşluk olmadığı için bölünmez.
    // Son karakter sınır sayılmaz: ardından ne geleceği belli değil ("14." mi "14.30" mu).
    private static bool IsBoundary(string text, int i)
    {
        if (text[i] == '\n')
            return true;
        if (i + 1 >= text.Length || !char.IsWhiteSpace(text[i + 1]))
            return false;
        return text[i] is '.' or '!' or '?' or '…' or ';' or ':';
    }
}
