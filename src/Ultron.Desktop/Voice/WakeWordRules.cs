using System.Globalization;
using System.Text.RegularExpressions;

namespace Ultron.Desktop.Voice;

/// <summary>
/// "Ultron" wake word'ünün kuralları (saf mantık, test edilebilir). Kapıcı benzetmesi: ses örnekleriyle karşılaştırma kapıdaki
/// gözetleme deliği (hızlı, ucuz, bazen yanılır), Whisper kontrolü ise kapıyı açmadan önce "kim o?" diye sormak.
/// Ölçüm (2026-10-27, kullanıcının mikrofon kaydı, <see cref="VoiceTemplateMatcher"/>): tek başına "Ultron" 2,19-2,55,
/// "Ultron, yarın hava nasıl?" 3,32; "Ultron" olmayan konuşma (kullanıcının "yarın hava nasıl?", başka sesler) 3,88-4,52.
/// İlk denenen sherpa-onnx İngilizce modeli kullanıcının "Ultron"larının 9'da 1'ini yakalayabildi (Whisper 9/9 duydu).
/// </summary>
public static partial class WakeWordRules
{
    /// <summary>
    /// En yakın ses örneğine uzaklık bunun altındaysa aday (Whisper'a sorulur). 3,5 idi; canlı denemede bir "Ultron" 3,94'te kaldı
    /// (2026-10-27). Yanlış adayları Whisper eliyor ("Evet" 3,36 → ret), bu yüzden eşik gevşek tutulur.
    /// </summary>
    public const double DistanceThreshold = 4.0;

    /// <summary>
    /// Çağrı kelimesi (Faz 26 kararı: uygulamanın adı). 2026-10-29'da bir süre Türkçe bir kelime denendi, çünkü "Ultron" Whisper'da
    /// "Ultra/Altron" diye yazılıyor ve 2 hecesi Türkçe konuşmaya benziyordu (kayıtlarda 16 kabul, 832 ret). Bu yüzden örnek sayısı
    /// arttırıldı (<see cref="SamplesWanted"/>) ve Whisper'ın duyduğu biçimler aşağıda tanınır.
    /// </summary>
    public const string Word = "Ultron";

    /// <summary>Kayıtta istenen ses örneği sayısı (15: "Ultron" Türkçe konuşmaya benzediği için ses imzası daha çok örnekle sağlamlaşır).</summary>
    public const int SamplesWanted = 15;

    /// <summary>Ses örneklerinin klasörü (yedeğe girer: voices). Kelimeye özel: başka kelimenin örnekleri karışmaz.</summary>
    public static readonly string SamplesFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "voices", "wake", "ultron");

    /// <summary>Örnek dosyası adı: ultron-1.wav, ultron-2.wav…</summary>
    public const string SamplePrefix = "ultron-";

    /// <summary>
    /// Windows'un varsayılan mikrofonu WaveIn listesinde hangisi (adlar 31 karakterde kesik: "Mikrofon Dizisi (Realtek(R) Aud").
    /// Bulunamazsa ilk cihaz. Saf mantık: <see cref="Microphone.Pick"/> kullanır.
    /// </summary>
    public static (int Number, string Name) ChooseMicrophone(IReadOnlyList<string> waveInNames, string? defaultName)
    {
        if (waveInNames.Count == 0)
            return (-1, "mikrofon yok");
        if (!string.IsNullOrWhiteSpace(defaultName))
            for (var i = 0; i < waveInNames.Count; i++)
                if (defaultName.StartsWith(waveInNames[i], StringComparison.OrdinalIgnoreCase) || waveInNames[i].StartsWith(defaultName, StringComparison.OrdinalIgnoreCase))
                    return (i, waveInNames[i]);
        return (0, waveInNames[0]);
    }

    /// <summary>Sıradaki örnek numarası (ultron-3.wav varsa 4). 2026-10-29 hatası: önek uzunluğu yanlış okunuyordu, hep 1 çıkıp aynı dosyanın üstüne yazıyordu.</summary>
    public static int NextSampleNumber(IEnumerable<string> files) =>
        files.Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
            .Select(n => n.StartsWith(SamplePrefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(n[SamplePrefix.Length..], out var k) ? k : 0)
            .DefaultIfEmpty(0).Max() + 1;

    /// <summary>Konuşma başlangıcı sayılması için öncesinde bu kadar sessizlik olmalı.</summary>
    public const int QuietBeforeMs = 250;

    /// <summary>Konuşma parçası bu kadar sessizlikle biter (kelime arası kısa duraklar parçayı bölmesin).</summary>
    public const int SegmentEndSilenceMs = 250;

    /// <summary>Bir tetiklemeden sonra bu süre yeni tetikleme sayılmaz (aynı "Ultron" iki kez yakalanmasın).</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    /// <summary>Her sesli cevaptan sonra (düğme, sağ tık ya da "Ultron") ek soru için bekleme (2026-10-29: 10 sn; önce 8 sn ve sadece "Ultron" ile).</summary>
    public const int FollowUpMs = 10000;

    /// <summary>Gürültülü ortam: bu kadar sürede (ms) bu kadar aday reddedilirse algılama bir süre dinlenir (WakeWordListener).</summary>
    public const int NoisyWindowMs = 30_000, NoisyRejections = 3, NoisyPauseMs = 20_000;

    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>
    /// Whisper'ın yazdığında "Ultron" (ya da duyulduğu biçimlerden biri) geçiyor mu: "Ultron", "Altron", "Ultran", "Uztran",
    /// "Old Tron", "Ultron'un"… "Patron", "Ultra", "transfer", "elektron" geçmez.
    /// </summary>
    public static bool IsWakeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var plain = text.ToLower(Turkish).Replace('ü', 'u').Replace('ö', 'o').Replace('ı', 'i').Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g');
        return Words().Split(plain).Any(w => w.Length > 0 && Name().IsMatch(w));
    }

    [GeneratedRegex(@"[^a-z]+")]
    private static partial Regex Words();

    // Ön ek (ul/al/ol/hul/uz/old…) + tr + a/o/u + n + isteğe bağlı Türkçe ek. "tron" tek başına da sayılır ("Old Tron").
    [GeneratedRegex(@"^(h?[uoa]l[dt]?|[uoa]z|old)?tr[aou]n(u|un|a|da|dan|la|cum|cugum|e|i)?$")]
    private static partial Regex Name();
}
