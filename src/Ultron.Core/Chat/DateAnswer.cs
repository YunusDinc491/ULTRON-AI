using System.Globalization;
using System.Text.RegularExpressions;
using Ultron.Core.Text;

namespace Ultron.Core.Chat;

/// <summary>
/// "Bugünden 10 gün sonra hangi gün?", "3 hafta sonra ayın kaçı?", "yarın hangi gün?" gibi takvim hesapları kodla cevaplanır.
/// Gece sınavı (tur5 e03): "bugün" kelimesi soruyu Güncel'e (web) götürdü, model "Pazartesi" dedi (doğrusu Cumartesi).
/// Hesap makinesi gibi: model tahmin etmez, takvim sayar.
/// </summary>
public static partial class DateAnswer
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Soru bir takvim hesabıysa cevap; değilse null.</summary>
    public static string? TryAnswer(string userText, DateTime now)
    {
        var text = TurkishText.Normalize(userText);
        if (!AsksDate().IsMatch(text))
            return null;

        DateTime target;
        if (Offset().Match(text) is { Success: true } m)
        {
            var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var sign = m.Groups[3].Value == "once" ? -1 : 1;
            target = m.Groups[2].Value switch
            {
                "hafta" => now.Date.AddDays(7 * n * sign),
                "ay" => now.Date.AddMonths(n * sign),
                "yil" => now.Date.AddYears(n * sign),
                _ => now.Date.AddDays(n * sign),
            };
        }
        else if (Regex.IsMatch(text, @"\bobur gun\b"))
            target = now.Date.AddDays(2);
        else if (Regex.IsMatch(text, @"\byarin\b"))
            target = now.Date.AddDays(1);
        else if (Regex.IsMatch(text, @"\bdun\b"))
            target = now.Date.AddDays(-1);
        else if (Regex.IsMatch(text, @"\bbugun\b"))
            target = now.Date;
        else
            return null;

        return $"{target.ToString("d MMMM yyyy dddd", Turkish)}.";
    }

    /// <summary>"hangi gün", "ne gün", "ayın kaçı", "hangi tarih", "tarih ne", "günlerden ne".</summary>
    [GeneratedRegex(@"\b(hangi gun|ne gun|gunlerden ne|ayin kaci|hangi tarih|tarih ne|tarihi ne|hangi gune|ne zaman denk|haftanin hangi)")]
    private static partial Regex AsksDate();

    /// <summary>"10 gün sonra", "3 hafta önce", "2 ay sonra".</summary>
    [GeneratedRegex(@"\b(\d{1,4}) (gun|hafta|ay|yil) (sonra|once)\b")]
    private static partial Regex Offset();
}
