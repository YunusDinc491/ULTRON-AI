namespace Ultron.Core.Web;

/// <summary>Bir arama sonucu: başlık, adres ve arama motorunun gösterdiği kısa özet.</summary>
/// <param name="Content">Arama servisi sayfanın metnini de verdiyse (Tavily, Faz 20): sayfayı ayrıca indirmeye gerek kalmaz.</param>
public sealed record WebResult(string Title, string Url, string Snippet, string? Content = null)
{
    /// <summary>Servisin verdiği metin işe yarayacak uzunlukta mı (menü kırıntısı değil).</summary>
    public bool HasContent => Content is { Length: >= 300 };
}

/// <summary>İnternette arama. Core, aramanın Tavily ile mi SearXNG ile mi yapıldığını bilmez.</summary>
public interface IWebSearch
{
    Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sadece belli sitelerde arar ("okulun sitesinde akademik takvim"). Desteklemeyen arama "site:" ifadesiyle aranır
    /// (Google, Bing, DuckDuckGo bunu anlar).
    /// </summary>
    Task<IReadOnlyList<WebResult>> SearchAsync(string query, IReadOnlyList<string> domains, CancellationToken cancellationToken = default) =>
        SearchAsync(domains.Count == 0 ? query : $"{query} site:{domains[0]}", cancellationToken);
}

/// <summary>Bir sayfadaki bağlantı: görünen yazısı ve tam adresi.</summary>
public sealed record WebLink(string Text, string Url);

/// <summary>Okunan sayfa: düz metin ve (HTML ise) içindeki bağlantılar. PDF'te bağlantı yoktur.</summary>
public sealed record WebPage(string Url, string Text, IReadOnlyList<WebLink> Links, bool IsPdf = false);

/// <summary>Bir web sayfasının okunabilir metni.</summary>
public interface IWebPageReader
{
    /// <summary>
    /// Sayfanın düz metni (kısaltılmış). Sayfa okunamazsa (zaman aşımı, desteklenmeyen tür, izin verilmeyen adres) null:
    /// tek bir sayfanın hatası tüm aramayı bozmamalı.
    /// </summary>
    Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Daha uzun metin (Ultron Medium'un akıllı okuması: sayfanın tamamından soruyla ilgili bölümler seçilir). Desteklemeyen
    /// okuyucu normal (kısa) okumaya düşer.
    /// </summary>
    Task<string?> ReadTextAsync(string url, int maxChars, CancellationToken cancellationToken = default) => ReadTextAsync(url, cancellationToken);

    /// <summary>
    /// Metin + bağlantılar (Faz 20: link takibi, site tarama). PDF'ler de okunur; JavaScript'le dolan sayfalar gerekirse
    /// görünmez tarayıcıyla açılır. Desteklemeyen okuyucu bağlantısız metin döner.
    /// </summary>
    async Task<WebPage?> ReadPageAsync(string url, int maxChars, CancellationToken cancellationToken = default) =>
        await ReadTextAsync(url, maxChars, cancellationToken) is { } text ? new WebPage(url, text, []) : null;
}

public static class WebResults
{
    /// <summary>Sonucun metni: arama servisi verdiyse o (indirmeden), yoksa sayfa okunur; o da olmazsa arama özeti.</summary>
    public static async Task<string> TextAsync(this IWebPageReader reader, WebResult result, int maxChars, CancellationToken cancellationToken)
    {
        if (result.HasContent)
            return result.Content!.Length <= maxChars ? result.Content : result.Content[..maxChars] + "…";
        return await reader.ReadTextAsync(result.Url, maxChars, cancellationToken) ?? result.Snippet;
    }
}
