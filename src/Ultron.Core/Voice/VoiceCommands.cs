using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ultron.Core.Text;

namespace Ultron.Core.Voice;

/// <summary>Hızlı yoldan çalışacak komut: araç adı, argümanları ve sesli kısa onay.</summary>
public sealed record QuickCommand(string Tool, JsonElement Arguments, string Confirmation);

/// <summary>
/// Hızlı komut yolu (Faz 23, kullanıcı: "5 sn düşünüyor, 3 sn'de açıyor, 10 sn sonra kötü bir sesle yaptığını söylüyor"): sık ve kalıbı
/// belli komutlar yapay zekâya sorulmadan kodla anlaşılır — YouTube aç/durdur/devam/sar/sonraki, ses düzeyi, müzik, pencere öne getirme.
/// Şüpheli her cümle (soru, uzun cümle) normal yoldan gider. Benzetme: "ışığı aç" diyene düşünmeden düğmeye basmak; "ışık neden
/// yanmıyor?" diyene ise düşünüp cevap vermek.
/// </summary>
public static partial class VoiceCommands
{
    private static readonly string[] Fillers =
        ["ultron", "lutfen", "hemen", "simdi", "bir", "bi", "su", "sunu", "bana", "acar", "misin", "musun", "mi", "mu", "ya", "hadi", "tamam"];

    private static readonly string[] QuestionWords = [" neden ", " nasil ", " nedir ", " ne zaman ", " kac ", " hangi ", " kim ", " niye "];

    public static QuickCommand? Parse(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText) || userText.Length > 120 || userText.Contains('?'))
            return null;
        var t = " " + TurkishText.Normalize(userText) + " ";
        if (QuestionWords.Any(w => t.Contains(w, StringComparison.Ordinal)) || t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12)
            return null;
        var video = Has(t, " youtube", " video", " videoyu", " klip");

        // "YouTube'da Tarkan Şımarık aç", "Sezen Aksu Gidiyorum şarkısını aç/çal"
        if ((video || Has(t, " sarkisini ", " sarkiyi ", " sarki ")) && Has(t, " ac ", " cal ", " oynat ", " acsana ", " acar misin ", " cal "))
        {
            var query = Query(userText);
            if (query.Length >= 2)
                return Make("youtube", new { islem = "ac", sorgu = query }, "Açıyorum.");
        }

        // "videoyu 10 dakika ileri sar", "2 dakika geri al", "12. dakikaya git"
        if (Seek().Match(t) is { Success: true } seek)
        {
            var amount = int.Parse(seek.Groups["n"].Value, CultureInfo.InvariantCulture) * (seek.Groups["u"].Value.StartsWith("dakika", StringComparison.Ordinal) ? 60 : 1);
            var mode = seek.Groups["to"].Success || Has(t, " git ", " gel ", " atla ") ? "git" : Has(t, " geri ") ? "geri" : "ileri";
            if (Has(t, " sar ", " ileri ", " geri ", " git ", " atla ") || seek.Groups["to"].Success)
                return Make("youtube", new { islem = "sar", saniye = amount, mod = mode }, "Sardım.");
        }

        // Ses düzeyi: "sesi %30 yap", "sesi 40'a getir", "sesi kıs/aç/kapat"
        if (Has(t, " sesi ", " ses "))
        {
            if (Percent().Match(t) is { Success: true } p && Has(t, " yap ", " getir ", " ayarla ", " olsun ", " cek ", " indir ", " ac "))
                return Make("ses", new { islem = "ayarla", deger = int.Parse(p.Groups["n"].Value, CultureInfo.InvariantCulture) }, "Tamam.");
            if (Has(t, " kis ", " azalt ", " dusur ", " alcalt "))
                return Make("ses", new { islem = "kis" }, "Tamam.");
            if (Has(t, " kapat ", " sessize al "))
                return Make("ses", new { islem = "kapat" }, "Tamam.");
            if (Has(t, " ac ", " artir ", " yukselt ", " acsana "))
                return Make("ses", new { islem = "ac" }, "Tamam.");
        }
        if (Has(t, " sessize al "))
            return Make("ses", new { islem = "kapat" }, "Tamam.");

        // Oynatma: YouTube sözü varsa YouTube'un kendi kontrolü, yoksa klavyenin medya tuşları (Spotify da çalışır).
        var music = Has(t, " muzik", " sarki", " parca", " sarkiyi", " muzigi");
        if (video || music || t.Trim().Split(' ').Length <= 3)
        {
            if (Has(t, " durdur ", " duraklat ", " dur ", " beklet "))
                return video ? Make("youtube", new { islem = "durdur" }, "Durdurdum.") : Make("medya", new { islem = "oynat_durdur" }, "Tamam.");
            if (Has(t, " devam ", " devam et ", " oynat ", " baslat "))
                return video ? Make("youtube", new { islem = "devam" }, "Devam.") : Make("medya", new { islem = "oynat_durdur" }, "Tamam.");
            if (Has(t, " sonraki ", " gec ", " siradaki ", " atla "))
                return video ? Make("youtube", new { islem = "sonraki" }, "Sıradakine geçtim.") : Make("medya", new { islem = "sonraki" }, "Sıradakine geçtim.");
            if (Has(t, " onceki "))
                return Make("medya", new { islem = "onceki" }, "Tamam.");
        }

        // "Chrome'u öne getir", "Spotify'a geç"
        if (FrontWindow().Match(t) is { Success: true } w)
            return Make("pencere", new { islem = "one_getir", pencere = w.Groups["name"].Value.Trim() }, "Tamam.");

        return null;
    }

    private static bool Has(string text, params string[] words) => words.Any(w => text.Contains(w, StringComparison.Ordinal));

    private static QuickCommand Make(string tool, object args, string confirmation) =>
        new(tool, JsonSerializer.SerializeToElement(args), confirmation);

    /// <summary>"YouTube'da Tarkan Şımarık aç" → "Tarkan Şımarık" (asıl yazımla; komut kelimeleri atılır).</summary>
    internal static string Query(string userText)
    {
        var words = userText.Split([' ', ',', '.', '!'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w =>
            {
                var n = TurkishText.Normalize(w);
                return n.Length > 0 && !Fillers.Contains(n) && !n.StartsWith("youtube", StringComparison.Ordinal)
                    && n is not ("ac" or "cal" or "oynat" or "acsana" or "sarkisini" or "sarkiyi" or "sarki" or "videosunu" or "videoyu" or "video" or "klibini" or "klip" or "da" or "de" or "dan" or "den");
            });
        return string.Join(' ', words).Trim();
    }

    /// <summary>"10 dakika", "30 saniye", "12 dakikaya" (normalleştirilmiş: "12." → "12").</summary>
    [GeneratedRegex(@" (?<n>\d{1,3}) ?(?<u>dakika|saniye)(?<to>ya|ye|sina|sine)?\b")]
    private static partial Regex Seek();

    [GeneratedRegex(@" (?<n>\d{1,3}) ")]
    private static partial Regex Percent();

    [GeneratedRegex(@"^ (?<name>[a-z0-9 ]{2,30}?) ?(u|i|yu|yi|a|e|ya|ye)? (one getir|one al|ac ve one getir|e gec|a gec|ya gec|ye gec) $")]
    private static partial Regex FrontWindow();
}
