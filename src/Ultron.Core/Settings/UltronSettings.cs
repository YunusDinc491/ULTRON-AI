using System.Globalization;
using Ultron.Core.Text;

namespace Ultron.Core.Settings;

/// <summary>Kullanıcının konuşarak değiştirebildiği bir ayarın kaydı (anahtar → değer).</summary>
public sealed class UserSetting
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public interface ISettingsStore
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Ayarın tanımı: anahtar, Türkçe adı, varsayılanı, açıklaması ve değeri doğrulayıp düzgün biçime çeviren fonksiyon.</summary>
public sealed record SettingDefinition(string Key, string Name, string Default, string Help, Func<string, string?> Normalize);

/// <summary>
/// Ultron'un konuşarak ayarlanabilen tercihleri (kullanıcı: "her şey için sana gelemem, onunla konuşarak da yapabileyim").
/// Sadece kişisel tercihler; yetki ayarları buradan değişmez (Faz 12: onay kartı).
/// </summary>
public sealed class UltronSettings(ISettingsStore store, TimeProvider time)
{
    public const string BriefingTime = "brifing_saati";
    public const string BriefingDays = "brifing_gunleri";
    public const string QuietStart = "sessiz_baslangic";
    public const string QuietEnd = "sessiz_bitis";
    public const string DefaultCity = "varsayilan_sehir";
    public const string SuggestionsPerDay = "oneri_sayisi";
    public const string WakeForBriefing = "uyandirma";
    public const string SecondaryBackup = "yedek_ikinci_yer";
    public const string WakeWord = "wake_word";
    public const string FormulaSize = "formul_boyutu";
    public const string TextSize = "yazi_boyutu";
    public const string UserAddress = "hitap";
    public const string ProProvider = "pro_hesap";

    /// <summary>"Büyüt/küçült" bir adımda kaç puan değiştirir (yüzde).</summary>
    public const int SizeStep = 15;

    /// <summary>Görünüm boyutlarının sınırları: okunaksız ya da ekrana sığmaz hale gelmesin.</summary>
    public static (int Min, int Max) SizeRange(string key) => key == FormulaSize ? (70, 220) : (80, 160);

    /// <summary>İç kayıt (kullanıcı değiştirmez): son brifingin verildiği gün.</summary>
    public const string LastBriefingDate = "son_brifing";

    public static readonly IReadOnlyList<SettingDefinition> Definitions =
    [
        new(BriefingTime, "Sabah brifingi saati", "07:30", "SS:DD biçiminde saat", ParseTime),
        new(BriefingDays, "Brifing günleri", "hafta_ici", "hafta_ici, her_gun, hafta_sonu ya da kapali", ParseDays),
        new(QuietStart, "Sessiz saatler başlangıcı", "23:00", "Bu saatten sonra önemsiz/önemli hatırlatmalar sesli okunmaz", ParseTime),
        new(QuietEnd, "Sessiz saatler bitişi", "07:00", "Bu saatte sessiz saatler biter", ParseTime),
        new(DefaultCity, "Varsayılan şehir", "İstanbul", "Hava durumu ve brifing için", ParseCity),
        new(SuggestionsPerDay, "Günlük öneri sınırı", "sinirsiz", "Ultron'un günde en fazla kaç kez kendiliğinden öneride bulunacağı: sinirsiz (sadece gerçekten işe yarar olanlar, aralarında en az 1 saat), 1-10 ya da 0 = kapalı", ParseSuggestionCount),
        new(WakeForBriefing, "Sabah uyandırma", "kapali", "acik: brifing günlerinde bilgisayar uykudaysa brifingden 2 dk önce uyandırılır (Windows'ta uyandırma zamanlayıcılarına izin verilmeli)", ParseOnOff),
        new(SecondaryBackup, "İkinci yedek yeri", "yok", "Günlük yedeğin ayrıca kopyalanacağı klasör (harici disk, OneDrive klasörü; ör. E:\\UltronYedek) ya da yok", ParseBackupFolder),
        new(WakeWord, "\"Ultron\" deyince dinleme", "acik", "acik: mikrofon sürekli çağrı kelimesi \"Ultron\"u bekler (ses bilgisayardan çıkmaz, kaydedilmez); kapali: sadece düğmeyle ya da simgeye sağ tıkla", ParseOnOff),
        new(UserAddress, "Hitap", "", "Ultron'un sana nasıl seslendiği (ör. adın ya da \"patron\"); yok = isimle hitap etmez", ParseAddress),
        new(ProProvider, "Ultron Pro hesabı", "otomatik", "Ultron Pro'nun araştırmayı yaptırdığı hesap: otomatik (kurulu ve girişli ilk araç), claude, codex ya da gemini", ParseProProvider),
        new(FormulaSize, "Formül boyutu", "100", "Ekrandaki matematik formüllerinin boyutu, yüzde (70-220)", v => ParsePercent(v, FormulaSize)),
        new(TextSize, "Yazı boyutu", "100", "Cevap yazılarının boyutu, yüzde (80-160)", v => ParsePercent(v, TextSize)),
    ];

    /// <summary>Hitap: harf, boşluk, kesme işareti ve tire; en fazla 30 karakter. "yok"/"kapalı"/boş → "" (isimle hitap yok).</summary>
    internal static string? ParseAddress(string value)
    {
        var text = value.Trim().Trim('"', '\'', '“', '”').Trim();
        if (text.Length == 0 || TurkishText.Normalize(text) is "yok" or "kapali" or "bos" or "hicbiri")
            return "";
        return text.Length <= 30 && text.All(c => char.IsLetter(c) || c is ' ' or '\'' or '-') ? text : null;
    }

    /// <summary>"otomatik", "claude", "codex", "gemini" (Türkçe karşılıklarıyla); başkası geçersiz.</summary>
    internal static string? ParseProProvider(string value) => TurkishText.Normalize(value).Trim() switch
    {
        "otomatik" or "oto" or "auto" or "hangisi varsa" => "otomatik",
        "claude" or "claude code" or "anthropic" => "claude",
        "codex" or "openai" or "chatgpt" => "codex",
        "gemini" or "google" => "gemini",
        _ => null,
    };

    /// <summary>Cümleyi hitapla başlatır: "Ece, hatırlatmamı istemiştin…"; hitap yoksa "Hatırlatmamı istemiştin…".</summary>
    public static string Addressed(string? address, string sentence) =>
        string.IsNullOrWhiteSpace(address)
            ? sentence.Length == 0 ? sentence : char.ToUpper(sentence[0], new CultureInfo("tr-TR")) + sentence[1..]
            : $"{address.Trim()}, {sentence}";

    /// <summary>"120", "%120", "yüzde 120" → "120" (sınır içindeyse).</summary>
    internal static string? ParsePercent(string value, string key)
    {
        var digits = new string(TurkishText.Normalize(value).Where(char.IsDigit).ToArray());
        var (min, max) = SizeRange(key);
        return int.TryParse(digits, out var n) && n >= min && n <= max ? n.ToString(CultureInfo.InvariantCulture) : null;
    }

    public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var all = await store.GetAllAsync(cancellationToken);
        return all.TryGetValue(key, out var value) ? value : Definitions.FirstOrDefault(d => d.Key == key)?.Default ?? "";
    }

    public async Task<IReadOnlyList<(SettingDefinition Definition, string Value)>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await store.GetAllAsync(cancellationToken);
        return Definitions.Select(d => (d, all.TryGetValue(d.Key, out var v) ? v : d.Default)).ToList();
    }

    /// <returns>Hata mesajı; başarılıysa null.</returns>
    public async Task<string?> SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.FirstOrDefault(d => d.Key == key);
        if (definition is null)
            return $"'{key}' adında bir ayar yok.";
        if (definition.Normalize(value) is not { } normalized)
            return $"'{value}' {definition.Name} için geçerli değil ({definition.Help}).";
        await store.SetAsync(key, normalized, time.GetUtcNow().UtcDateTime, cancellationToken);
        Changed?.Invoke(key);
        return null;
    }

    /// <summary>Bir ayar konuşarak değişti (ör. brifing saati → uyandırma görevi güncellenir).</summary>
    public event Action<string>? Changed;

    /// <summary>İç kayıtlar (doğrulamasız): son brifing günü gibi.</summary>
    public Task SetInternalAsync(string key, string value, CancellationToken cancellationToken = default) =>
        store.SetAsync(key, value, time.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<string?> GetInternalAsync(string key, CancellationToken cancellationToken = default) =>
        (await store.GetAllAsync(cancellationToken)).GetValueOrDefault(key);

    public async Task<TimeOnly> GetTimeAsync(string key, CancellationToken cancellationToken = default) =>
        TimeOnly.ParseExact(await GetAsync(key, cancellationToken), "HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Sessiz saatlerde mi? (23:00-07:00 gibi gece yarısını aşan aralık desteklenir.)</summary>
    public async Task<bool> IsQuietAsync(DateTime local, CancellationToken cancellationToken = default)
    {
        var start = await GetTimeAsync(QuietStart, cancellationToken);
        var end = await GetTimeAsync(QuietEnd, cancellationToken);
        var now = TimeOnly.FromDateTime(local);
        return start == end ? false : start < end ? now >= start && now < end : now >= start || now < end;
    }

    /// <summary>Bugün brifing günü mü?</summary>
    public async Task<bool> IsBriefingDayAsync(DateTime local, CancellationToken cancellationToken = default)
    {
        var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        return await GetAsync(BriefingDays, cancellationToken) switch
        {
            "her_gun" => true,
            "hafta_ici" => !weekend,
            "hafta_sonu" => weekend,
            _ => false,
        };
    }

    // ---- Doğrulama: modelin ve kullanıcının farklı yazımlarını tek biçime çevirir ----

    /// <summary>"8", "8:00", "08.30", "7:5" → "08:00", "08:30"…</summary>
    internal static string? ParseTime(string value)
    {
        var parts = value.Trim().Replace('.', ':').Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var h) || h is < 0 or > 23)
            return null;
        var m = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out m) || m is < 0 or > 59))
            return null;
        return $"{h:00}:{m:00}";
    }

    internal static string? ParseDays(string value) => TurkishText.Normalize(value).Replace(' ', '_') switch
    {
        "hafta_ici" or "haftaici" or "is_gunleri" => "hafta_ici",
        "her_gun" or "hergun" or "hepsi" => "her_gun",
        "hafta_sonu" or "haftasonu" => "hafta_sonu",
        "kapali" or "kapat" or "yok" or "hic" => "kapali",
        _ => null,
    };

    internal static string? ParseSuggestionCount(string value) => TurkishText.Normalize(value) switch
    {
        "0" or "kapali" or "kapat" or "yok" or "hic" or "sifir" => "0",
        "sinirsiz" or "limitsiz" or "acik" or "ac" or "istedigi kadar" or "serbest" => "sinirsiz",
        "bir" => "1",
        "iki" => "2",
        "uc" => "3",
        var n when int.TryParse(n, out var count) && count is >= 1 and <= 10 => count.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    internal static string? ParseOnOff(string value) => TurkishText.Normalize(value) switch
    {
        "acik" or "ac" or "evet" or "aktif" or "true" or "1" => "acik",
        "kapali" or "kapat" or "hayir" or "pasif" or "false" or "0" => "kapali",
        _ => null,
    };

    /// <summary>"yok" ya da tam klasör yolu ("E:\UltronYedek"). Windows klasörü kabul edilmez.</summary>
    internal static string? ParseBackupFolder(string value)
    {
        var path = value.Trim().Trim('"');
        if (TurkishText.Normalize(path) is "yok" or "kapali" or "kapat" or "hic" or "iptal")
            return "yok";
        if (path.Length is < 3 or > 200 || !Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return null;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return windows.Length > 0 && full.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ? null : full;
    }

    internal static string? ParseCity(string value)
    {
        var city = value.Trim();
        return city.Length is >= 2 and <= 60 && city.All(c => char.IsLetter(c) || c is ' ' or '-' or '\'') ? city : null;
    }
}
