using Ultron.Core.Text;

namespace Ultron.Core.Memories;

/// <summary>
/// Kullanıcının sorusundan anahtar kelime araması için kök önekleri çıkarır.
/// Türkçe eklemeli bir dil ("kedimin", "dersleri"); tam kök bulmak yerine kelimenin
/// ilk 4 harfini önek olarak arıyoruz: "kedimin" → "kedi*", "dersleri" → "ders*".
/// </summary>
public static class KeywordQuery
{
    public const int PrefixLength = 4;

    // Normalleştirilmiş (ş→s, ı→i…) yaygın kelimeler: anlam taşımaz, her kayıtla eşleşir.
    private static readonly HashSet<string> StopWords =
    [
        "ama", "ancak", "bana", "beni", "benim", "bir", "biz", "bize", "bizim", "bu", "cok", "daha", "diye", "en",
        "gibi", "hangi", "hangisi", "hem", "hep", "icin", "ile", "kadar", "kim", "kime", "kimin", "mi", "mu", "nasil",
        "ne", "neden", "nedir", "nerede", "nereye", "neydi", "neyi", "neye", "nicin", "niye", "olan", "olarak", "olur",
        "sana", "sen", "senin", "simdi", "su", "sey", "seyi", "seyler", "var", "veya", "ve", "yani", "yok", "zaten",
        "acaba", "artik", "bugun", "yarin", "dun", "lazim", "gerek", "etmek", "etmeliyim", "yapmaliyim", "misin", "musun",
        "miyim", "muyum", "hatirliyor", "hatirla", "soyle", "soyler", "bilgi",
    ];

    public static IReadOnlyList<string> Prefixes(string query) =>
        TurkishText.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= PrefixLength && !StopWords.Contains(w))
            .Select(w => w[..PrefixLength])
            .Distinct()
            .ToList();
}
