using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Documents;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Web;

/// <summary>
/// Arama sonucundaki sayfayı indirip düz metne çevirir. Faz 20: PDF'ler de okunur (akademik takvim, yönetmelik çoğu sitede PDF;
/// önceden "HTML değil" diye atlanıyordu), bağlantılar toplanır (link takibi, site tarama), JavaScript'le dolan sayfalar
/// görünmez Edge ile açılır.
/// Güvenlik: sadece http/https ve sadece internetteki adresler. Arama sonuçları dışarıdan geldiği için
/// "http://127.0.0.1:11434" gibi bir adres yerel servislerimize (Ollama, SearXNG) istek attırmak için kullanılabilir (SSRF).
/// </summary>
internal sealed class HttpWebPageReader(
    HttpClient http,
    IOptions<WebOptions> options,
    ILogger<HttpWebPageReader> logger,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolveHost = null,
    IHeadlessBrowser? browser = null,
    IDocumentTextExtractor? extractor = null) : IWebPageReader
{
    private const int MaxRedirects = 3;

    static HttpWebPageReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // windows-1254 gibi eski Türkçe kodlamalar

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveHost = resolveHost ?? Dns.GetHostAddressesAsync;

    public Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default) => ReadTextAsync(url, options.Value.MaxPageChars, cancellationToken);

    public async Task<string?> ReadTextAsync(string url, int maxChars, CancellationToken cancellationToken = default) =>
        (await ReadPageAsync(url, maxChars, cancellationToken))?.Text;

    public async Task<WebPage?> ReadPageAsync(string url, int maxChars, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Skip(url, "geçersiz adres");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(o.PageTimeoutSeconds * 2)); // PDF'ler büyük olabilir

            // Yönlendirmeleri kendimiz izliyoruz (HttpClient'ta otomatik yönlendirme kapalı): internetteki bir adres
            // 302 ile 127.0.0.1'e yönlendirirse her adımda adres kontrolü yapılabilsin.
            HttpResponseMessage? response = null;
            for (var hop = 0; ; hop++)
            {
                if (uri.Scheme is not ("http" or "https"))
                    return Skip(url, "http/https değil");
                if (!await IsPublicHostAsync(uri, timeout.Token))
                    return Skip(url, $"yerel/özel ağ adresi ({uri.Host})");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(o.UserAgent);
                request.Headers.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9,en;q=0.5");
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if ((int)response.StatusCode is < 300 or > 399 || response.Headers.Location is null)
                    break;
                if (hop >= MaxRedirects)
                {
                    response.Dispose();
                    return Skip(url, "çok fazla yönlendirme");
                }

                uri = new Uri(uri, response.Headers.Location);
                response.Dispose();
            }

            using var _ = response;
            if (!response.IsSuccessStatusCode)
                return Skip(url, $"HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType == "application/pdf" || (mediaType is "application/octet-stream" or null && uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                return await ReadPdfAsync(uri, response, maxChars, timeout.Token);
            if (mediaType == "text/plain") // robots.txt, düz metin duyurular
            {
                var plain = GetEncoding(response.Content.Headers.ContentType?.CharSet).GetString(await ReadLimitedAsync(response, o.MaxPageBytes, timeout.Token));
                return new WebPage(uri.AbsoluteUri, plain.Length <= maxChars ? plain : plain[..maxChars], []);
            }
            if (mediaType is not ("text/html" or "application/xhtml+xml"))
                return Skip(url, $"desteklenmeyen tür ({mediaType})");

            var bytes = await ReadLimitedAsync(response, o.MaxPageBytes, timeout.Token);
            var html = GetEncoding(response.Content.Headers.ContentType?.CharSet).GetString(bytes);
            var (text, links) = HtmlText.Parse(html, uri, maxChars);

            // JavaScript'le dolan sayfa: görünmez Edge ile son hali (aynı adres; iç ağa gitmesin diye adres yukarıda denetlendi).
            if (browser is { Available: true } && HtmlText.LooksScriptRendered(html, text)
                && await browser.RenderAsync(uri, cancellationToken) is { } rendered)
            {
                var (renderedText, renderedLinks) = HtmlText.Parse(rendered, uri, maxChars);
                if (renderedText.Length > text.Length)
                {
                    logger.LogInformation("Sayfa görünmez Edge ile okundu ({Before} → {After} karakter): {Url}", text.Length, renderedText.Length, uri);
                    (text, links) = (renderedText, renderedLinks.Count > 0 ? renderedLinks : links);
                }
            }
            return new WebPage(uri.AbsoluteUri, text, links);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or SocketException && !cancellationToken.IsCancellationRequested)
        {
            return Skip(url, ex.GetType().Name);
        }
    }

    private async Task<WebPage?> ReadPdfAsync(Uri uri, HttpResponseMessage response, int maxChars, CancellationToken cancellationToken)
    {
        if (extractor is null)
            return Skip(uri.AbsoluteUri, "PDF okuyucu yok");
        var bytes = await ReadLimitedAsync(response, options.Value.MaxPdfBytes, cancellationToken);
        try
        {
            var pages = extractor.Extract(DocumentKind.Pdf, bytes);
            var sb = new StringBuilder();
            foreach (var page in pages.Where(p => !string.IsNullOrWhiteSpace(p.Text)))
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(page.Text.Trim()).Append('\n');
                if (sb.Length >= maxChars)
                    break;
            }
            var text = sb.ToString().Trim();
            if (text.Length == 0)
                return Skip(uri.AbsoluteUri, "PDF'te yazı yok (taranmış olabilir)");
            var name = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
            logger.LogInformation("PDF okundu ({Pages} sayfa, {Chars} karakter): {Url}", pages.Count, text.Length, uri);
            return new WebPage(uri.AbsoluteUri, $"{name} (PDF)\n" + (text.Length <= maxChars ? text : text[..maxChars] + "…"), [], IsPdf: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Skip(uri.AbsoluteUri, "PDF açılamadı: " + ex.GetType().Name);
        }
    }

    private WebPage? Skip(string url, string reason)
    {
        logger.LogInformation("Sayfa okunmadı ({Reason}): {Url}", reason, url);
        return null;
    }
    private async Task<bool> IsPublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(uri.Host, out var literal)
            ? [literal]
            : await _resolveHost(uri.DnsSafeHost, cancellationToken);

        return addresses.Length > 0 && addresses.All(IsPublic);
    }

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);

        var b = address.GetAddressBytes();
        return !(b[0] == 10                                   // 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)     // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254)                  // link-local
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)    // CGNAT (Tailscale vb.)
            || b[0] == 0);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < maxBytes && (read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            buffer.Write(chunk, 0, (int)Math.Min(read, maxBytes - buffer.Length));
        return buffer.ToArray();
    }

    private static Encoding GetEncoding(string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
