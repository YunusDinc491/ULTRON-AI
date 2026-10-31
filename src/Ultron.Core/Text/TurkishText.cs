using System.Globalization;
using System.Text;

namespace Ultron.Core.Text;

public static class TurkishText
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>
    /// Arama için sadeleştirir: Türkçe küçük harf (İ→i, I→ı), sonra ı→i, ş→s, ç→c, ğ→g, ö→o, ü→u, â→a;
    /// harf/rakam dışındaki her şey (kesme işareti dahil) boşluk olur. "Pamuk'un ŞEHRİ" → "pamuk un sehri".
    /// Böylece "Isparta", "ısparta" ve "Ispartada" yazımları aynı köke iner.
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLower(Turkish))
        {
            sb.Append(c switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ç' => 'c',
                'ğ' => 'g',
                'ö' => 'o',
                'ü' => 'u',
                'â' or 'á' or 'à' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ when char.IsLetterOrDigit(c) => c,
                _ => ' ',
            });
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
