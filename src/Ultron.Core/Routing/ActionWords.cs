using Ultron.Core.Text;

namespace Ultron.Core.Routing;

/// <summary>
/// Bir mesajda "bilgisayarda/dünyada işlem yap" fiili var mı? Router'ın Komut kararını denetlemek için.
/// Kelime kökleri normalleştirilmiş biçimde (ç→c, ı→i…): "hatırlat" → "hatirlat".
/// </summary>
public static class ActionWords
{
    // Bu köklerle başlayan kelimeler işlem bildirir: hatırlat-ır, kapat-ır mısın, gönder-sene…
    private static readonly string[] Stems =
    [
        "hatirlat", "alarm", "kapat", "gonder", "baslat", "calistir", "ayarla", "kaydet", "sil", "indir",
        "yukle", "tasi", "kopyala", "zamanlayici", "uyandir", "ara ", "not al", "iptal", "hesapla",
        "izin ver", "yetki", "guncelle", "yedekle",
    ];

    // "aç" kısa bir kök; "acil", "acaba" gibi kelimelerle karışmasın diye sadece bu biçimler.
    private static readonly HashSet<string> OpenForms =
    [
        "ac", "acar", "acarmisin", "acabilir", "acabilirmisin", "acsana", "acin", "acmani", "acmak", "acip", "acalim",
    ];

    public static bool Contains(string message)
    {
        var normalized = TurkishText.Normalize(message);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Any(w => OpenForms.Contains(w) || Stems.Any(s => !s.Contains(' ') && w.StartsWith(s, StringComparison.Ordinal)))
            || Stems.Where(s => s.Contains(' ')).Any(s => (" " + normalized + " ").Contains(" " + s, StringComparison.Ordinal));
    }
}
