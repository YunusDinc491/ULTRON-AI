using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Ultron.Core.Settings;
using Ultron.Core.Text;

namespace Ultron.Core.Self;

/// <summary>
/// Ultron'un kendisiyle ilgili istekler: "formülleri büyüt", "arayüzün rengini değiştir". Model bu isteklere eli olmadığı halde
/// "tamam, yaptım" diyordu (kullanıcı, ders ekranı: formüller aynı kaldı). Burada kod karar verir: görünüm ayarıysa gerçekten değiştirir,
/// değilse "bunu kendim yapamıyorum" der ve isteği Claude'un bakacağı geliştirme listesine yazar. Benzetme: resepsiyon ışığı
/// kendisi kısar, duvarı boyamak gerekiyorsa ustaya iş emri yazar; "boyadım" demez.
/// </summary>
public sealed partial class SelfRequests(UltronSettings settings, IImprovementLog log, TimeProvider time)
{
    private static readonly string[] Bigger = [" buyut", " buyuk", " buyusun", " artir"];
    private static readonly string[] Smaller = [" kucult", " kucuk", " azalt", " ufak"];
    private static readonly string[] Reset = ["eski haline", "eskisi gibi", "varsayilan", "normal boyut", "normale don", "sifirla", "ilk haline"];
    private static readonly string[] QuestionWords = [" mi ", " mu ", " midir ", " mudur ", " neden ", " nicin "];
    private static readonly string[] More = ["biraz daha", "daha da", "bir tik daha", "biraz daha buyut", "biraz daha kucult", "devam et", "bir daha"];

    /// <summary>Ultron'un kendi ekranı/özelliği (lessondaki "ekran kartı", "buton yap" gibi şeylere takılmasın diye dar tutuldu).</summary>
    private static readonly string[] SelfThings =
    [
        "arayuz", "temani", "temasini", " tema ", "gorunum", "arka plan", "cekirdeg", "cekirdek", "ana ekran", "ders ekran", "sohbet ekran",
        "gelisim pano", "ogrenme panel", "yazma kutusu", "yazma yeri", "paket secici", "ultron un", "ultronun", "kendini", " sana ozellik",
        " ozellik ekle", " ozelligi ekle", " ozelligini ekle", "ozellik olsun", "sesini", "sesin ", "renklerini", "rengini",
    ];

    private static readonly string[] ChangeVerbs =
    [
        " yap", " olsun", " degistir", " ekle", " kaldir", " koy", " getir", " cikar", " gelistir", " guncelle", " duzelt", " ayarla",
        " buyut", " kucult", " artir", " azalt", " koyulastir", " acilastir",
    ];

    /// <summary>Yazılım isteği ("uygulamama özellik ekle") Ultron'un kendisi değil.</summary>
    private static readonly string[] OtherThings = [" uygulama", " site", " proje", " kod", " oyun", " api", " react", " python"];

    /// <param name="previousAssistant">Önceki cevap "Formülleri büyüttüm" ise "biraz daha" aynı yönde devam eder.</param>
    /// <returns>Kullanıcıya söylenecek cevap; istek Ultron'un kendisiyle ilgili değilse null.</returns>
    public async Task<string?> HandleAsync(string userText, string source, string? previousAssistant = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userText) || userText.Length > 300)
            return null;
        var text = " " + TurkishText.Normalize(userText) + " ";

        if (Appearance(text, userText, previousAssistant) is { } change)
            return await ApplyAsync(change, cancellationToken);
        if (!IsAboutUltron(text))
            return null;

        var open = await log.AddAsync(userText.Trim(), source, time.GetLocalNow().DateTime, cancellationToken);
        return "Bunu kendim değiştiremiyorum: kendi kodumu ve ekranımı değiştirme yetkim yok. Görünümde sadece formül ve yazı boyutunu " +
            "ayarlayabiliyorum (ör. \"formülleri büyüt\", \"yazıyı küçült\", \"eski haline getir\").\n\n" +
            $"İsteğini **geliştirme listesine** ekledim (listede {open} istek var). Claude'a \"listeye bak\" dediğinde o yapacak.";
    }

    /// <summary>Ultron'un ekranı, özelliği ya da kendisiyle ilgili bir değişiklik isteği mi? (normalleştirilmiş, boşluklu metin)</summary>
    internal static bool IsAboutUltron(string text) =>
        SelfThings.Any(w => text.Contains(w, StringComparison.Ordinal))
        && ChangeVerbs.Any(w => text.Contains(w, StringComparison.Ordinal))
        && !OtherThings.Any(w => text.Contains(w, StringComparison.Ordinal));

    internal sealed record Change(string Key, int Direction, int? Exact);

    /// <summary>"formülleri büyüt" → formul_boyutu +1; "yazıyı %120 yap" → yazi_boyutu 120; "eski haline getir" → varsayılan (0).</summary>
    /// <param name="raw">Normalleştirilmemiş metin: "%130" işareti normalleştirmede kayboluyor.</param>
    internal static Change? Appearance(string text, string raw, string? previousAssistant)
    {
        var formula = text.Contains("formul", StringComparison.Ordinal) || text.Contains("denklem", StringComparison.Ordinal);
        var writing = text.Contains(" yazi", StringComparison.Ordinal) && !text.Contains(" yazilim", StringComparison.Ordinal)
            || text.Contains(" harf", StringComparison.Ordinal) || text.Contains(" font", StringComparison.Ordinal) || text.Contains("punto", StringComparison.Ordinal);

        // "Biraz daha": önceki cevap bir boyut değişikliğiyse aynı ayar, aynı yön.
        if (!formula && !writing && previousAssistant is not null && More.Any(w => text.Contains(w, StringComparison.Ordinal)) && text.Length < 40)
        {
            var previous = " " + TurkishText.Normalize(previousAssistant) + " ";
            var previousKey = previous.Contains("formul", StringComparison.Ordinal) ? Settings.UltronSettings.FormulaSize
                : previous.Contains("yaziyi", StringComparison.Ordinal) || previous.Contains("yazi boyut", StringComparison.Ordinal) ? Settings.UltronSettings.TextSize : null;
            if (previousKey is not null && (previous.Contains("buyuttum", StringComparison.Ordinal) || previous.Contains("kuculttum", StringComparison.Ordinal)))
            {
                var direction = Smaller.Any(w => text.Contains(w, StringComparison.Ordinal)) ? -1
                    : Bigger.Any(w => text.Contains(w, StringComparison.Ordinal)) ? 1
                    : previous.Contains("buyuttum", StringComparison.Ordinal) ? 1 : -1;
                return new Change(previousKey, direction, null);
            }
            return null;
        }
        if (!formula && !writing)
            return null;

        // Bilgi sorusu değil, istek: "formülü büyük harfle mi yazılır?" gibi sorular geçmesin diye yön kelimesi + istek biçimi aranır.
        var exact = Percent().Match(raw.ToLower(new CultureInfo("tr-TR"))) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
        var reset = Reset.Any(w => text.Contains(w, StringComparison.Ordinal));
        var dir = Bigger.Any(w => text.Contains(w, StringComparison.Ordinal)) ? 1 : Smaller.Any(w => text.Contains(w, StringComparison.Ordinal)) ? -1 : 0;
        if (exact is null && !reset && dir == 0)
            return null;
        if (text.Contains(" nedir", StringComparison.Ordinal) || text.Contains(" nasil hesap", StringComparison.Ordinal) || text.Contains(" turet", StringComparison.Ordinal))
            return null;
        if (dir != 0 && exact is null && !reset && !ChangeVerbs.Any(w => text.Contains(w, StringComparison.Ordinal)))
            return null;
        // "Bu formül büyük basınçta geçerli mi?" soru; "büyütür müsün?" istek.
        if (QuestionWords.Any(w => text.Contains(w, StringComparison.Ordinal)) && !text.Contains(" musun", StringComparison.Ordinal) && !text.Contains(" misin", StringComparison.Ordinal))
            return null;

        var key = formula ? Settings.UltronSettings.FormulaSize : Settings.UltronSettings.TextSize;
        return new Change(key, reset ? 0 : dir, reset ? null : exact);
    }

    private async Task<string> ApplyAsync(Change change, CancellationToken cancellationToken)
    {
        var definition = UltronSettings.Definitions.First(d => d.Key == change.Key);
        var before = int.Parse(await settings.GetAsync(change.Key, cancellationToken), CultureInfo.InvariantCulture);
        var target = change.Direction == 0 && change.Exact is null ? int.Parse(definition.Default, CultureInfo.InvariantCulture)
            : change.Exact ?? before + change.Direction * UltronSettings.SizeStep;
        var (min, max) = UltronSettings.SizeRange(change.Key);
        target = Math.Clamp(target, min, max);
        var what = change.Key == UltronSettings.FormulaSize ? "Formülleri" : "Yazıyı";

        if (target == before)
            return target == max ? $"{what} zaten en büyük boyutta (%{max})." : target == min ? $"{what} zaten en küçük boyutta (%{min})." : $"{what} zaten %{target} boyutunda.";
        if (await settings.SetAsync(change.Key, target.ToString(CultureInfo.InvariantCulture), cancellationToken) is { } error)
            return error;
        var verb = change.Direction == 0 && change.Exact is null ? "eski haline getirdim" : target > before ? "büyüttüm" : "küçülttüm";
        return $"{what} {verb}: %{before} → %{target}. Ekranda hemen değişti; \"biraz daha\" ya da \"eski haline getir\" diyebilirsin.";
    }

    /// <summary>"%130", "% 130", "yüzde 130".</summary>
    [GeneratedRegex(@"(?:%\s?|yüzde\s?)(\d{2,3})\b")]
    private static partial Regex Percent();
}

/// <summary>Ultron'un yapamadığı isteklerin listesi: Claude bir sonraki oturumda buradan yapar.</summary>
public interface IImprovementLog
{
    /// <returns>Listede bekleyen (yapılmamış) istek sayısı.</returns>
    Task<int> AddAsync(string request, string source, DateTime atLocal, CancellationToken cancellationToken = default);
}

public sealed class ImprovementOptions
{
    public const string SectionName = "Improvements";

    /// <summary>Markdown dosyası: Claude okur, yaptıklarını [x] ile işaretler.</summary>
    public string FilePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "gelistirme-istekleri.md");
}

/// <summary>Geliştirme isteklerini düz bir markdown dosyasına yazar ("- [ ] tarih · kaynak · istek"); aynı istek iki kez yazılmaz.</summary>
public sealed class FileImprovementLog(IOptions<ImprovementOptions> options) : IImprovementLog
{
    private const string Header = "# Ultron geliştirme istekleri\n\nUltron'un kendisi yapamadığı istekler. Claude yaptıklarını `[x]` ile işaretler.\n\n";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> AddAsync(string request, string source, DateTime atLocal, CancellationToken cancellationToken = default)
    {
        var path = options.Value.FilePath;
        var clean = request.ReplaceLineEndings(" ").Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var lines = File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
            var open = lines.Where(l => l.StartsWith("- [ ]", StringComparison.Ordinal)).ToList();
            if (open.Any(l => l.EndsWith(" · " + clean, StringComparison.OrdinalIgnoreCase)))
                return open.Count;
            if (lines.Length == 0)
                await File.WriteAllTextAsync(path, Header, cancellationToken);
            await File.AppendAllTextAsync(path, $"- [ ] {atLocal:yyyy-MM-dd HH:mm} · {source} · {clean}\n", cancellationToken);
            return open.Count + 1;
        }
        finally
        {
            _gate.Release();
        }
    }
}
