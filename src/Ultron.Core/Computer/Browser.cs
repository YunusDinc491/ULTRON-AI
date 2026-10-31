using System.Net;
using System.Text;
using System.Text.Json;
using Ultron.Core.Text;
using Ultron.Core.Tools;

namespace Ultron.Core.Computer;

/// <summary>Sayfadaki tıklanabilir/doldurulabilir öğe: [numara] tür "etiket".</summary>
/// <param name="Sensitive">Şifre, kart, CVV, IBAN, TC kimlik alanı: Ultron yazmaz (yasak).</param>
/// <param name="Submit">Formu gönderen düğme: onaylı.</param>
/// <param name="Purchase">"Satın al", "Ödeme", "Sepete ekle" gibi: yasak.</param>
public sealed record BrowserElement(int Number, string Kind, string Label, bool Sensitive, bool Submit, bool Purchase);

public sealed record BrowserPage(string Url, string Title, string Text, IReadOnlyList<BrowserElement> Elements);

/// <summary>
/// Ultron'un kendi tarayıcısı (Faz 21, tasarım kararı): ayrı profil (kullanıcının oturumları/şifreleri yok), görünür pencere.
/// Gezinme ve okuma serbest; form gönderme onaylı; şifre, ödeme, satın alma yasak.
/// </summary>
public interface IUltronBrowser
{
    BrowserPage? Current { get; }

    Task<BrowserPage> OpenAsync(string url, CancellationToken cancellationToken = default);

    Task<BrowserPage> SnapshotAsync(CancellationToken cancellationToken = default);

    Task<BrowserPage> ClickAsync(int number, CancellationToken cancellationToken = default);

    Task<BrowserPage> TypeAsync(int number, string text, CancellationToken cancellationToken = default);

    Task<BrowserPage> BackAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// "okul sitesine gir, duyurular sayfasındaki son 3 başlığı oku", "şu formu doldur ama gönderme". Model adım adım çağırır:
/// her sonuçta sayfanın metni ve numaralı öğeleri döner, bir sonraki adımı buna göre seçer.
/// </summary>
public sealed class BrowserTool(IUltronBrowser browser) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "tarayici",
        "Ultron'un kendi tarayıcısını (görünür pencere, kullanıcının hesapları yok) kullanır. islem: ac (adres), oku (sayfanın metni), tikla (öğe numarası), yaz (öğe numarası + metin), geri, kapat. Her sonuçta sayfanın metni ve [numara]lı öğeleri gelir; sıradaki adımı onlara göre seç. Formu gönderen düğmeye tıklamak onay ister; şifre/kart alanına yazmak ve satın alma yasaktır.",
        """
        {"type":"object","properties":{
          "islem":{"type":"string","enum":["ac","oku","tikla","yaz","geri","kapat"]},
          "adres":{"type":"string","description":"ac için: tam adres ya da alan adı (ör. ornek.edu.tr)"},
          "numara":{"type":"integer","description":"tikla/yaz için öğe numarası"},
          "metin":{"type":"string","description":"yaz için"}
        },"required":["islem"]}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var action = ToolArgs.String(arguments, "islem");
        if (action == "ac")
            return Task.FromResult(Url(arguments) is { } u && IsPublic(u) ? PermissionLevel.Serbest : PermissionLevel.Yasak);
        if (action is "tikla" or "yaz" && Element(arguments) is { } el)
        {
            if (el.Purchase || (action == "yaz" && el.Sensitive))
                return Task.FromResult(PermissionLevel.Yasak);
            if (action == "tikla" && el.Submit)
                return Task.FromResult(PermissionLevel.Onayli);
            return Task.FromResult(action == "yaz" ? PermissionLevel.Bildirerek : PermissionLevel.Serbest);
        }
        return Task.FromResult(PermissionLevel.Serbest);
    }

    public string Describe(JsonElement arguments) =>
        Element(arguments) is { } el ? $"Tarayıcıda gönder: [{el.Number}] \"{el.Label}\" · {browser.Current?.Title} ({browser.Current?.Url})" : "Tarayıcı işlemi";

    private BrowserElement? Element(JsonElement arguments) =>
        ToolArgs.Long(arguments, "numara") is { } n ? browser.Current?.Elements.FirstOrDefault(e => e.Number == n) : null;

    internal static Uri? Url(JsonElement arguments)
    {
        if (ToolArgs.String(arguments, "adres") is not { } text)
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>Yerel/özel ağ adresleri açılmaz (Ultron'un kendi servisleri, modem arayüzü).</summary>
    internal static bool IsPublic(Uri uri)
    {
        if (uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !uri.Host.Contains('.'))
            return false;
        if (!IPAddress.TryParse(uri.Host, out var ip))
            return true;
        var b = ip.GetAddressBytes();
        return !(b.Length == 4 && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)));
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var page = ToolArgs.String(arguments, "islem") switch
            {
                "ac" => Url(arguments) is { } u ? await browser.OpenAsync(u.AbsoluteUri, cancellationToken) : null,
                "oku" => await browser.SnapshotAsync(cancellationToken),
                "tikla" => ToolArgs.Long(arguments, "numara") is { } n ? await browser.ClickAsync((int)n, cancellationToken) : null,
                "yaz" => ToolArgs.Long(arguments, "numara") is { } m && ToolArgs.String(arguments, "metin") is { } t ? await browser.TypeAsync((int)m, t, cancellationToken) : null,
                "geri" => await browser.BackAsync(cancellationToken),
                "kapat" => null,
                _ => null,
            };
            if (ToolArgs.String(arguments, "islem") == "kapat")
            {
                await browser.CloseAsync(cancellationToken);
                return ToolResult.Success("Ultron tarayıcısı kapatıldı.");
            }
            return page is null ? ToolResult.Fail("Eksik bilgi: ac için adres, tikla/yaz için numara (ve metin) gerekli.") : ToolResult.Success(Summary(page));
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }

    /// <summary>Modele giden sayfa özeti: başlık, adres, metnin başı, numaralı öğeler (en fazla 40).</summary>
    public static string Summary(BrowserPage page)
    {
        var sb = new StringBuilder($"Sayfa: {page.Title} — {page.Url}\n");
        var text = page.Text.Length > 2500 ? page.Text[..2500] + "…" : page.Text;
        sb.AppendLine("Metin:").AppendLine(text);
        if (page.Elements.Count > 0)
        {
            sb.AppendLine("Öğeler:");
            foreach (var e in page.Elements.Take(40))
                sb.AppendLine($"[{e.Number}] {e.Kind} \"{e.Label}\"{(e.Submit ? " (gönderir — onay gerekir)" : "")}{(e.Sensitive ? " (hassas alan — yazılamaz)" : "")}{(e.Purchase ? " (satın alma — yasak)" : "")}");
            if (page.Elements.Count > 40)
                sb.AppendLine($"… ve {page.Elements.Count - 40} öğe daha");
        }
        return sb.ToString().TrimEnd();
    }

    private static readonly string[] SensitiveWords = ["password", "parola", "sifre", "card", "kart", "cvv", "cvc", "iban", "tckn", "kimlik", "cc number", "cc csc", "cc exp"];
    private static readonly string[] PurchaseWords = ["satin al", "sepete ekle", "sepeti onayla", "odeme", "ode ", "siparisi tamamla", "siparis ver", "checkout", "buy now", "pay now", "place order"];

    /// <summary>Etiket/tür/alan adından hassas ve satın alma öğelerini işaretler (tarayıcı katmanı da kullanır).</summary>
    public static (bool Sensitive, bool Purchase) Classify(string kind, string label, string fieldHints)
    {
        var hints = TurkishText.Normalize(kind + " " + fieldHints);
        var text = " " + TurkishText.Normalize(label) + " ";
        return (kind.Contains("password", StringComparison.OrdinalIgnoreCase) || SensitiveWords.Any(w => hints.Contains(w, StringComparison.Ordinal)),
            PurchaseWords.Any(w => text.Contains(w, StringComparison.Ordinal)));
    }
}
