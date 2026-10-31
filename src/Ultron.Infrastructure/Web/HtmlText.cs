using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Web;

/// <summary>HTML'den okunabilir düz metin ve bağlantılar çıkarır.</summary>
internal static partial class HtmlText
{
    // Okunacak metin taşımayan ya da gürültü olan bölümler.
    private const string NoiseSelector =
        "script, style, noscript, template, svg, canvas, iframe, form, button, select, nav, header, footer, aside, " +
        "[role=navigation], [role=banner], [role=contentinfo], [aria-hidden=true], .cookie, .cookies, .advert, .ads";

    private const string BlockSelector =
        "p, div, section, article, main, h1, h2, h3, h4, h5, h6, li, dt, dd, br, tr, td, th, blockquote, pre, figcaption";

    /// <summary>Okunmaya değmeyen dosyalar (resim, arşiv, ses).</summary>
    private static readonly string[] SkipExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".zip", ".rar", ".mp3", ".mp4", ".avi", ".exe", ".ico", ".css", ".js", ".xml", ".ics"];

    public static string Extract(string html, int maxChars) => Parse(html, baseUrl: null, maxChars).Text;

    /// <summary>Metin + sayfadaki bağlantılar. Bağlantılar gürültü silinmeden toplanır: "Akademik Takvim" linki çoğu sitede menüdedir.</summary>
    public static (string Text, IReadOnlyList<WebLink> Links) Parse(string html, Uri? baseUrl, int maxChars)
    {
        var document = new HtmlParser().ParseDocument(html);

        var links = new List<WebLink>();
        if (baseUrl is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in document.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
            {
                var href = a.GetAttribute("href")?.Trim();
                if (string.IsNullOrEmpty(href) || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                    || !Uri.TryCreate(baseUrl, href, out var target) || target.Scheme is not ("http" or "https"))
                    continue;
                var clean = new UriBuilder(target) { Fragment = "" }.Uri.AbsoluteUri;
                if (SkipExtensions.Any(e => target.AbsolutePath.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || !seen.Add(clean))
                    continue;
                var label = Whitespace().Replace(a.TextContent ?? "", " ").Trim();
                if (label.Length == 0)
                    label = a.GetAttribute("title")?.Trim() ?? "";
                links.Add(new WebLink(label.Length > 120 ? label[..120] : label, clean));
                if (links.Count >= 400)
                    break;
            }
        }

        foreach (var element in document.QuerySelectorAll(NoiseSelector).ToList())
            element.Remove();

        // TextContent blok elemanları boşluksuz birleştirir ("<h1>Başlık</h1><p>Metin" → "BaşlıkMetin"); araya boşluk koy.
        foreach (var block in document.QuerySelectorAll(BlockSelector))
            block.Append(document.CreateTextNode(" "));

        // Asıl içerik genelde <article> veya <main> içinde; yoksa tüm gövde. Birden çok <article> olabilir (haber kartları):
        // en dolu olan seçilir; o da gövdenin küçük bir kısmıysa (Faz 20: bir üniversitenin takvim sayfası 2.046 karakter okunuyordu) <main>/gövde.
        var bodyLength = document.Body?.TextContent.Length ?? 0;
        var root = document.QuerySelectorAll("article, main").OrderByDescending(e => e.TextContent.Length).FirstOrDefault();
        if (root is null || root.TextContent.Length < bodyLength * 0.25)
            root = document.Body;
        var title = document.Title?.Trim();
        var text = Whitespace().Replace(root?.TextContent ?? "", " ").Trim();

        if (!string.IsNullOrEmpty(title) && !text.StartsWith(title, StringComparison.Ordinal))
            text = $"{title}\n{text}";

        return (text.Length <= maxChars ? text : text[..maxChars] + "…", links);
    }

    /// <summary>
    /// Sayfa JavaScript'le mi doluyor? Metin çok az ama sayfada uygulama kökü ya da çok script var (React/Vue/Angular siteleri).
    /// </summary>
    public static bool LooksScriptRendered(string html, string extractedText) =>
        extractedText.Length < 600
        && (html.Contains("id=\"root\"", StringComparison.OrdinalIgnoreCase) || html.Contains("id=\"app\"", StringComparison.OrdinalIgnoreCase)
            || html.Contains("ng-app", StringComparison.OrdinalIgnoreCase) || html.Contains("__NEXT_DATA__", StringComparison.Ordinal)
            || ScriptTag().Count(html) >= 8 || extractedText.Length < 200);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<script\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();
}
