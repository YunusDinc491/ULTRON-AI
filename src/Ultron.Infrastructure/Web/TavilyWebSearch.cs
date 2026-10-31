using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Settings;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Web;

/// <summary>
/// Tavily (Faz 20, tasarım kararı: öncelik Tavily, ücretsiz kota bitince yerel SearXNG). Yapay zekâ için tasarlanmış arama:
/// sonuçla birlikte sayfanın temizlenmiş metnini de verir (JavaScript'li sayfalar dahil), sayfayı ayrıca indirmeye gerek kalmaz.
/// Her arama "basic" (1 kredi). Anahtar sohbete değil dosyaya/ortam değişkenine konur (bkz. <see cref="WebOptions.TavilyKeyFile"/>).
/// </summary>
internal sealed class TavilyWebSearch(HttpClient http, IOptions<WebOptions> options, ILogger<TavilyWebSearch> logger)
{
    public enum Outcome { Ok, NoKey, QuotaExhausted, BadKey, Failed }

    /// <summary>Anahtar: önce dosya (kullanıcı Not Defteri'yle koyar), sonra TAVILY_API_KEY ortam değişkeni, sonra ayar dosyası.</summary>
    public string? ApiKey()
    {
        var o = options.Value;
        try
        {
            if (File.Exists(o.TavilyKeyFile) && File.ReadAllText(o.TavilyKeyFile).Trim() is { Length: > 10 } fromFile)
                return fromFile;
        }
        catch (IOException) { }
        return Environment.GetEnvironmentVariable("TAVILY_API_KEY") is { Length: > 10 } env ? env.Trim()
            : string.IsNullOrWhiteSpace(o.TavilyApiKey) ? null : o.TavilyApiKey.Trim();
    }

    public async Task<(Outcome Outcome, IReadOnlyList<WebResult> Results)> SearchAsync(string query, IReadOnlyList<string> domains, CancellationToken cancellationToken)
    {
        if (ApiKey() is not { } key)
            return (Outcome.NoKey, []);
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
        {
            Content = JsonContent.Create(new TavilyRequest(query, "basic", o.TavilyMaxResults, "markdown", domains.Count > 0 ? domains : null)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            switch ((int)response.StatusCode)
            {
                case 432 or 433:
                    logger.LogWarning("Tavily kotası doldu ({Status}); bu ay SearXNG kullanılacak", (int)response.StatusCode);
                    return (Outcome.QuotaExhausted, []);
                case 401 or 403:
                    logger.LogWarning("Tavily anahtarı geçersiz ({Status})", (int)response.StatusCode);
                    return (Outcome.BadKey, []);
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Tavily cevap vermedi ({Status}), SearXNG'ye geçiliyor", (int)response.StatusCode);
                return (Outcome.Failed, []);
            }
            var body = await response.Content.ReadFromJsonAsync<TavilyResponse>(timeout.Token);
            var results = (body?.Results ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Url) && !string.IsNullOrWhiteSpace(r.Title))
                .Select(r => new WebResult(r.Title!.Trim(), r.Url!, r.Content?.Trim() ?? "", Clean(r.RawContent, o.TavilyMaxContentChars)))
                .ToList();
            logger.LogInformation("Tavily: '{Query}'{Domains} → {Count} sonuç", query, domains.Count > 0 ? " (" + string.Join(",", domains) + ")" : "", results.Count);
            return (Outcome.Ok, results);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Tavily'ye ulaşılamadı ({Error}), SearXNG'ye geçiliyor", ex.GetType().Name);
            return (Outcome.Failed, []);
        }
    }

    private static string? Clean(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim();
        return raw.Length <= max ? raw : raw[..max];
    }

    private sealed record TavilyRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("max_results")] int MaxResults,
        [property: JsonPropertyName("include_raw_content")] string IncludeRawContent,
        [property: JsonPropertyName("include_domains"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? IncludeDomains);

    private sealed record TavilyResponse([property: JsonPropertyName("results")] List<TavilyResult>? Results);

    private sealed record TavilyResult(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("raw_content")] string? RawContent);
}

/// <summary>
/// Ultron'un arama kapısı: önce Tavily; anahtar yoksa, kota dolduysa (o ayın sonuna kadar), hata verdiyse ya da sonuç çıkmadıysa
/// yerel SearXNG (Brave, DuckDuckGo, Startpage, Bing, Mojeek…). Benzetme: önce ücretli kargo, kotası dolunca kendi aracınla.
/// </summary>
internal sealed class FallbackWebSearch(TavilyWebSearch tavily, SearxngWebSearch searxng, UltronSettings settings, TimeProvider time, ILogger<FallbackWebSearch> logger) : IWebSearch
{
    /// <summary>İç kayıt: Tavily kotasının dolduğu ay ("2026-10"); ay değişince Tavily yeniden denenir.</summary>
    public const string ExhaustedMonthKey = "tavily_kota_bitti_ay";

    private bool _badKeyWarned;

    public Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        SearchAsync(query, [], cancellationToken);

    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
    {
        var month = time.GetLocalNow().ToString("yyyy-MM");
        if (await settings.GetInternalAsync(ExhaustedMonthKey, cancellationToken) != month)
        {
            var (outcome, results) = await tavily.SearchAsync(query, domains, cancellationToken);
            if (outcome == TavilyWebSearch.Outcome.Ok && results.Count > 0)
                return results;
            if (outcome == TavilyWebSearch.Outcome.QuotaExhausted)
                await settings.SetInternalAsync(ExhaustedMonthKey, month, cancellationToken);
            if (outcome == TavilyWebSearch.Outcome.BadKey && !_badKeyWarned)
            {
                _badKeyWarned = true;
                logger.LogWarning("Tavily anahtarı kabul edilmedi; dosyadaki anahtarı kontrol et. Şimdilik SearXNG.");
            }
        }
        return domains.Count == 0 ? await searxng.SearchAsync(query, cancellationToken) : await searxng.SearchAsync($"{query} site:{domains[0]}", cancellationToken);
    }
}
