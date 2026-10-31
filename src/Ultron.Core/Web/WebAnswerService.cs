using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Text;

namespace Ultron.Core.Web;

public sealed record WebSearchPlan(string Query, string CacheKey, WebFreshness Freshness);

public sealed record WebSource(int Number, string Title, string Url, string Text);

/// <summary>Güncel bir soru için hazırlanan web bağlamı: ya önbellekten bir kayıt ya da okunmuş kaynaklar.</summary>
public sealed record WebContext(WebSearchPlan Plan, WebCacheEntry? Cached, IReadOnlyList<WebSource> Sources)
{
    public bool FromCache => Cached is not null;
}

/// <summary>
/// Güncel sorular için akış: planla (sorgu + gizlilik + anahtar) → önbellek → yoksa ara ve oku → modele kaynak olarak ver → cevabı önbelleğe yaz.
/// </summary>
public sealed class WebAnswerService(
    IChatModel chat,
    IWebSearch search,
    IWebPageReader reader,
    IWebCacheStore cache,
    IOptions<WebAnswerOptions> options,
    TimeProvider time,
    ILogger<WebAnswerService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <param name="previousAssistantMessage">"Peki euro?", "Ya yarın?" gibi devam soruları için bağlam.</param>
    public async Task<WebContext> PrepareAsync(string userMessage, string? previousAssistantMessage = null, CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(userMessage, previousAssistantMessage, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;

        var cached = await cache.FindValidAsync(plan.CacheKey, nowUtc, cancellationToken);
        if (cached is not null && Pro.ProService.LooksUnsure(cached.Answer))
            cached = null; // "bulamadım" cevabı önbellekten verilmez, yeniden aranır
        if (cached is not null)
        {
            logger.LogInformation("Önbellekten: {Key}", plan.CacheKey);
            return new WebContext(plan, cached, []);
        }

        await cache.LogOutgoingAsync(plan.Query, nowUtc, cancellationToken);
        var results = await search.SearchAsync(plan.Query, cancellationToken);

        // Sayfaları aynı anda oku: her biri 10 sn'ye kadar sürebilir, sırayla okumak bekletir.
        var top = results.Take(options.Value.PagesToRead).ToList();
        var texts = await Task.WhenAll(top.Select(r => reader.TextAsync(r, 4000, cancellationToken))); // Tavily metni verdiyse indirilmez

        var sources = top
            .Select((r, i) => (r, text: texts[i])) // sayfa okunamadıysa arama özeti
            .Where(x => !string.IsNullOrWhiteSpace(x.text))
            .Select((x, i) => new WebSource(i + 1, x.r.Title, x.r.Url, x.text))
            .ToList();

        logger.LogInformation("Web: '{Query}' → {Count} kaynak", plan.Query, sources.Count);
        return new WebContext(plan, null, sources);
    }

    /// <summary>Modelin system prompt'una eklenecek not.</summary>
    public string BuildNote(WebContext context) => context switch
    {
        { Cached: { } entry } => WebPrompts.CachedNote(entry, time.GetUtcNow().UtcDateTime),
        { Sources.Count: > 0 } => WebPrompts.SourcesNote(context.Sources),
        _ => WebPrompts.NoResults,
    };

    /// <summary>Kaynaklardan üretilen cevabı süresiyle önbelleğe yazar. Önbellekten gelen veya kaynaksız cevap yazılmaz.</summary>
    public async Task SaveAsync(WebContext context, string answer, CancellationToken cancellationToken = default)
    {
        if (context.FromCache || context.Sources.Count == 0 || string.IsNullOrWhiteSpace(answer))
            return;

        var nowUtc = time.GetUtcNow().UtcDateTime;
        await cache.AddAsync(new WebCacheEntry
        {
            CacheKey = context.Plan.CacheKey,
            Query = context.Plan.Query,
            Answer = answer.Trim(),
            SourceUrls = string.Join("\n", context.Sources.Select(s => s.Url)),
            Freshness = context.Plan.Freshness,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + options.Value.Ttl(context.Plan.Freshness),
        }, cancellationToken);
    }

    internal async Task<WebSearchPlan> PlanAsync(string userMessage, string? previousAssistantMessage, CancellationToken cancellationToken)
    {
        var now = time.GetLocalNow().DateTime;
        var user = previousAssistantMessage is null
            ? userMessage
            : $"Önceki asistan mesajı: {previousAssistantMessage}\nKullanıcının sorusu: {userMessage}";
        var json = await chat.CompleteJsonAsync(
            [new(Role.System, WebPrompts.Plan(now)), new(Role.User, user)],
            WebPrompts.PlanSchema,
            cancellationToken);
        return ParsePlan(json, userMessage);
    }

    /// <summary>Bozuk alanlarda güvenli varsayılanlar: sorgu yoksa mesajın kendisi, süre anlaşılmazsa en kısa (saat).</summary>
    internal static WebSearchPlan ParsePlan(string json, string fallbackQuery)
    {
        var raw = JsonSerializer.Deserialize<RawPlan>(json, JsonOptions);
        var query = string.IsNullOrWhiteSpace(raw?.Sorgu) ? fallbackQuery : raw.Sorgu.Trim();
        var key = TurkishText.Normalize(string.IsNullOrWhiteSpace(raw?.Anahtar) ? query : raw.Anahtar);
        var freshness = Enum.TryParse<WebFreshness>(raw?.Sure, ignoreCase: true, out var f) ? f : WebFreshness.Saat;
        return new WebSearchPlan(query, key, freshness);
    }

    private sealed record RawPlan(string? Sorgu, string? Anahtar, string? Sure);
}
