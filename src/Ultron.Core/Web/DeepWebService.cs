using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Ultron.Core.Chat;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Core.Web;

/// <summary>
/// Ultron Medium'un web araştırması. Faz 19: Türkçe + İngilizce arama → 6 sayfa uzun okunur → ~900 karakterlik parçalardan
/// soruya ANLAMCA en yakınları seçilir (akıllı okuma). Faz 20 (kullanıcı: "yüzeysel sonuç istemiyorum", akademik takvim bulunamadı):
/// 2-3 farklı sorgu (yıl, özel adlar), takip edilen siteye daraltılmış arama, sayfadaki ilgili bağlantılara (PDF dahil) girme
/// ve "yeterli mi?" kontrolüyle en fazla 3 tur. En fazla ~1 dakika.
/// Benzetme: kitabın önsözü yerine içindekilerden ilgili bölümü bulup okumak; dipnottaki kaynağa da gidip bakmak.
/// </summary>
public sealed class DeepWebService(
    IChatModel chat,
    IWebSearch search,
    IWebPageReader reader,
    IWebCacheStore cache,
    IEmbeddingModel embedder,
    WebAnswerService web,
    TimeProvider time,
    ILogger<DeepWebService> logger,
    Sites.SiteService? sites = null)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private const int MaxPages = 6, PageChars = 20_000, PassageChars = 900, Passages = 10, PerPage = 3, MaxRounds = 3, LinksPerRound = 4, DirectReads = 2;

    public async Task<WebContext> PrepareAsync(string userMessage, string? previousAssistant, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        var basePlan = await web.PlanAsync(userMessage, previousAssistant, cancellationToken);
        var plan = basePlan with { CacheKey = "medium:" + basePlan.CacheKey };
        var nowUtc = time.GetUtcNow().UtcDateTime;
        // Önbellekteki "bulamadım" cevabı kullanılmaz (Faz 20 öncesinden kalmış olabilir): yeniden aranır.
        if (await cache.FindValidAsync(plan.CacheKey, nowUtc, cancellationToken) is { } cached && !Pro.ProService.LooksUnsure(cached.Answer))
            return new WebContext(plan, cached, []);

        var queries = await QueriesAsync(plan.Query, userMessage, cancellationToken);
        var domains = sites is null ? [] : await sites.DomainsForAsync(userMessage, cancellationToken);
        foreach (var q in queries)
            await cache.LogOutgoingAsync(q, nowUtc, cancellationToken);
        progress(domains.Count > 0 ? $"{queries.Count} sorguyla ve {domains[0]} içinde aranıyor" : $"{queries.Count} farklı sorguyla aranıyor");

        // Siteye daraltılmış arama önce (okulun kendi sayfası), sonra sorgular: her birinden en iyi sonuçlar dönüşümlü.
        var searches = queries.Select(q => SafeSearchAsync(q, [], budget.Token)).ToList();
        if (domains.Count > 0)
            searches.Insert(0, SafeSearchAsync(plan.Query, domains, budget.Token));
        var found = await Task.WhenAll(searches);
        var picked = Interleave(found).DistinctBy(r => r.Url).Take(MaxPages).ToList();
        if (picked.Count == 0)
            return new WebContext(plan, null, []);

        progress($"{picked.Count} kaynak okunuyor");
        var pages = await ReadAsync(picked, DirectReads, budget.Token);
        var question = userMessage + (queries.Count > 1 ? " / " + queries[^1] : "");
        IReadOnlyList<Passage> selected = [];

        for (var round = 1; ; round++)
        {
            // Sayfadaki ilgili bağlantılar ("Akademik Takvim 2026-2027.pdf"): arama sonucunda olmasa da içine girilir.
            var links = RelevantLinks(userMessage + " " + plan.Query, pages);
            if (links.Count > 0 && watch.Elapsed < Budget * 0.7)
            {
                progress($"{links.Count} bağlantının içine bakılıyor");
                pages.AddRange(await ReadAsync(links, links.Count, budget.Token));
            }
            selected = await SelectAsync(question, pages, cancellationToken); // süre dolsa da okunanlardan seçilir

            if (round >= MaxRounds || watch.Elapsed > Budget * 0.6 || await MissingQueryAsync(userMessage, selected, budget.Token) is not { } more)
                break;
            progress($"Eksik kısım için {round + 1}. tur aranıyor");
            await cache.LogOutgoingAsync(more, nowUtc, cancellationToken);
            var extra = (await SafeSearchAsync(more, domains, budget.Token)).Where(r => pages.All(p => p.Result.Url != r.Url)).Take(3).ToList();
            if (extra.Count == 0)
                break;
            pages.AddRange(await ReadAsync(extra, 1, budget.Token));
        }

        var sources = selected.GroupBy(p => p.Page.Result.Url)
            .Select((g, i) => new WebSource(i + 1, g.First().Page.Result.Title, g.Key, string.Join("\n…\n", g.OrderBy(p => p.Index).Select(p => p.Text))))
            .ToList();
        logger.LogInformation("Medium web: '{Query}' ({Queries} sorgu{Domains}) → {Pages} sayfa ({Pdf} PDF), {Passages} parça, {Seconds:0.0} sn",
            plan.Query, queries.Count, domains.Count > 0 ? ", site: " + domains[0] : "", pages.Count, pages.Count(p => p.IsPdf), selected.Count, watch.Elapsed.TotalSeconds);
        return new WebContext(plan, null, sources);
    }

    internal sealed record Page(WebResult Result, string Text, IReadOnlyList<WebLink> Links, bool IsPdf = false);

    internal sealed record Passage(Page Page, int Index, string Text);

    private static IEnumerable<WebResult> Interleave(IReadOnlyList<IReadOnlyList<WebResult>> lists)
    {
        for (var i = 0; i < 10; i++)
            foreach (var list in lists)
                if (i < list.Count)
                    yield return list[i];
    }

    /// <param name="direct">İlk kaç sonuç her durumda sayfasından okunur (bağlantıları için); diğerlerinde arama servisinin verdiği metin yeter.</param>
    private async Task<List<Page>> ReadAsync(IReadOnlyList<WebResult> results, int direct, CancellationToken cancellationToken)
    {
        var read = await Task.WhenAll(results.Select(async (r, i) =>
        {
            try
            {
                if (r.HasContent && i >= direct)
                    return new Page(r, r.Content!, []);
                if (await reader.ReadPageAsync(r.Url, PageChars, cancellationToken) is { } page)
                    return new Page(r, r.HasContent && r.Content!.Length > page.Text.Length ? r.Content! : page.Text, page.Links, page.IsPdf);
            }
            catch (OperationCanceledException)
            {
                // süre doldu: arama servisinin metni ya da özetiyle yetin
            }
            return new Page(r, r.Content ?? r.Snippet, []);
        }));
        return read.Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();
    }

    /// <summary>
    /// Okunan sayfalardaki, soruyla ilgili ve henüz okunmamış bağlantılar (en fazla 3). Kelime kökü eşleşmesi ("takvimi" ~ "takvim"),
    /// PDF ve sorudaki yıl artı puan; aynı siteden olanlar önde.
    /// </summary>
    internal static IReadOnlyList<WebResult> RelevantLinks(string question, IReadOnlyList<Page> pages)
    {
        var stems = TurkishText.Normalize(question).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w)).Select(w => w[..Math.Min(5, w.Length)]).Distinct().ToList();
        if (stems.Count == 0)
            return [];
        var years = question.Split(' ', '/', '-').Where(w => w.Length == 4 && w.StartsWith("20", StringComparison.Ordinal) && w.All(char.IsDigit)).ToList();
        var read = pages.Select(p => p.Result.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entities = Entities(question);
        return pages.SelectMany(p => p.Links.Select(l => (Link: l, From: p)))
            .Where(x => !read.Contains(x.Link.Url) && !IsShareOrSocial(x.Link.Url))
            .Select(x =>
            {
                // Adresin sadece yolu sayılır (sorgu kısmı paylaş linklerinde tüm başlığı taşıyor).
                var path = Uri.TryCreate(x.Link.Url, UriKind.Absolute, out var u) ? u.Host + u.AbsolutePath : x.Link.Url;
                var text = TurkishText.Normalize(x.Link.Text + " " + Uri.UnescapeDataString(path).Replace('-', ' ').Replace('_', ' ').Replace('/', ' ').Replace('.', ' '));
                var isPdf = u is not null && u.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var score = stems.Count(s => text.Contains(s, StringComparison.Ordinal))
                    + (isPdf ? 0.5 : 0)
                    + years.Count(y => text.Contains(y, StringComparison.Ordinal)) * 0.5
                    + (SameHost(x.Link.Url, x.From.Result.Url) ? 0.25 : 0)
                    + (u is not null && entities.Any(e => u.Host.ToLowerInvariant().Split('.').Contains(e)) ? 1 : 0); // sorudaki kurumun kendi sitesi
                return (x.Link, Score: score, IsPdf: isPdf);
            })
            .Where(x => x.Score >= Math.Min(2, stems.Count) || (x.Score >= 1.5 && x.IsPdf))
            .GroupBy(x => x.Link.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.MaxBy(x => x.Score))
            .OrderByDescending(x => x.Score).Take(LinksPerRound)
            .Select(x => new WebResult(x.Link.Text.Length > 0 ? x.Link.Text : x.Link.Url, x.Link.Url, ""))
            .ToList();
    }

    private static readonly HashSet<string> StopWords =
        ["nedir", "nasil", "neden", "hangi", "zaman", "kadar", "olan", "icin", "gibi", "daha", "sonra", "once", "nerede", "bana", "benim", "acaba", "misin", "soyle", "anlat", "ogren", "bilgi"];

    private static readonly string[] ShareMarkers = ["sharer", "/intent/", "share?", "/share", "whatsapp", "mailto:", "telegram.me", "t.me/share", "linkedin.com/share"];

    /// <summary>Paylaş düğmeleri ve sosyal medya: bilgi taşımaz, tüm sayfa başlığını adreste taşıdığı için kelime eşleşmesini kandırır.</summary>
    private static bool IsShareOrSocial(string url) =>
        ShareMarkers.Any(m => url.Contains(m, StringComparison.OrdinalIgnoreCase))
        || (Uri.TryCreate(url, UriKind.Absolute, out var u) && SocialHosts.Any(s => u.Host.Contains(s, StringComparison.OrdinalIgnoreCase)));

    private static bool SameHost(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub)
        && string.Equals(ua.Host.Replace("www.", ""), ub.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase);

    private const string QueriesSchema = """{"type":"object","properties":{"turkce":{"type":"array","items":{"type":"string"}},"ingilizce":{"type":"string"}},"required":["turkce","ingilizce"]}""";

    /// <summary>
    /// 2-3 farklı arama sorgusu: Türkçe ana sorgu + Türkçe alternatif (eşanlamlı, yıl, özel ad) + İngilizce (yerel konularda yok).
    /// Küçük model tüm cümleyi aratınca az sonuç çıkıyordu; anahtar kelimeli, farklı açılardan sorgular.
    /// </summary>
    private async Task<IReadOnlyList<string>> QueriesAsync(string baseQuery, string question, CancellationToken cancellationToken)
    {
        var list = new List<string> { baseQuery };
        try
        {
            var json = await chat.CompleteJsonAsync([
                new ChatMessage(Role.System, $"""
                    Bugün {time.GetLocalNow():dd.MM.yyyy}. Soruyu internette bulmak için arama sorguları yaz.
                    turkce: 1-2 kısa Türkçe sorgu (anahtar kelimeler; özel adları ve gerekiyorsa yılı koru; ilk sorgudan farklı bir açı ya da eşanlamlı ifade).
                    ingilizce: konu evrenselse (bilim, teknik, yazılım, dünya) kısa İngilizce sorgu; Türkiye'ye ya da bir kuruma özgüyse (okul, yerel haber, Türk mevzuatı) boş bırak.
                    """),
                new ChatMessage(Role.User, $"Soru: {question}\nİlk sorgu: {baseQuery}"),
            ], QueriesSchema, cancellationToken);
            var node = JsonNode.Parse(json);
            foreach (var q in node?["turkce"]?.AsArray().Select(n => n?.GetValue<string>()?.Trim()) ?? [])
                if (!string.IsNullOrWhiteSpace(q) && q.Length < 150 && list.Count < 2 && !list.Contains(q, StringComparer.OrdinalIgnoreCase))
                    list.Add(q);
            if (node?["ingilizce"]?.GetValue<string>()?.Trim() is { Length: > 2 and < 150 } en && !list.Contains(en, StringComparer.OrdinalIgnoreCase))
                list.Add(en);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Ek sorgular üretilemedi");
        }
        return list;
    }

    /// <summary>Sayfaları parçalara böler, soruya anlamca en yakın parçaları seçer (sayfa başına en fazla 3). Vektör yoksa baştan alır.</summary>
    internal async Task<IReadOnlyList<Passage>> SelectAsync(string question, IReadOnlyList<Page> pages, CancellationToken cancellationToken)
    {
        var passages = pages.SelectMany(p => Split(p.Text).Take(30).Select((t, i) => new Passage(p, i, t))).ToList();
        if (passages.Count <= Passages)
            return passages;
        try
        {
            var q = (await embedder.EmbedAsync([question], EmbeddingPurpose.Query, cancellationToken))[0];
            var vectors = await embedder.EmbedAsync(passages.Select(p => p.Text).ToList(), EmbeddingPurpose.Document, cancellationToken);
            var stems = Stems(question);
            var entities = Entities(question);
            return passages.Select((p, i) => (p, score: (vectors[i].Length == q.Length ? VectorMath.Cosine(q, vectors[i]) : 0) + Bonus(p, stems, entities)))
                .OrderByDescending(x => x.score)
                .GroupBy(x => x.p.Page.Result.Url).SelectMany(g => g.Take(PerPage))
                .OrderByDescending(x => x.score).Take(Passages).Select(x => x.p).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Parça seçimi vektörsüz yapıldı");
            return passages.GroupBy(p => p.Page.Result.Url).SelectMany(g => g.Take(2)).Take(Passages).ToList();
        }
    }

    private static readonly string[] SocialHosts = ["x.com", "twitter.com", "instagram.com", "facebook.com", "tiktok.com", "pinterest.", "youtube.com"];
    private static readonly string[] OfficialSuffixes = [".edu.tr", ".gov.tr", ".edu", ".gov", ".bel.tr", ".k12.tr"];

    /// <summary>
    /// Anlam benzerliğine ek puan (Faz 20 ölçümü: İTÜ'nün kendi takvim sayfası okunduğu halde tweet'ler ve başka üniversitelerin
    /// takvimleri seçildi; takvim tablosu anlamca "zayıf" görünüyor): sorudaki kelimeleri içeren parça, sorudaki kurumun alan adı
    /// (İstanbul Teknik Üniversitesi → itu), resmi uzantı artı; sosyal medya eksi.
    /// </summary>
    internal static double Bonus(Passage passage, IReadOnlyList<string> stems, IReadOnlyList<string> entities)
    {
        var text = TurkishText.Normalize(passage.Text);
        var lexical = stems.Count == 0 ? 0 : 0.12 * stems.Count(s => text.Contains(s, StringComparison.Ordinal)) / stems.Count;
        var host = Uri.TryCreate(passage.Page.Result.Url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
        var labels = host.Split('.');
        var entity = entities.Any(e => labels.Contains(e)) ? 0.08 : 0;
        var official = OfficialSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal)) ? 0.03 : 0;
        var social = SocialHosts.Any(s => host.Contains(s, StringComparison.Ordinal)) ? -0.10 : 0;
        return lexical + entity + official + social;
    }

    internal static IReadOnlyList<string> Stems(string question) =>
        TurkishText.Normalize(question).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w) && !w.All(char.IsDigit)).Select(w => w[..Math.Min(5, w.Length)]).Distinct().ToList();

    /// <summary>
    /// Sorudaki kurum adları alan adı biçiminde: "İstanbul Teknik Üniversitesi" → "itu", "ODTÜ" → "odtu", "Hacettepe" → "hacettepe".
    /// </summary>
    internal static IReadOnlyList<string> Entities(string question)
    {
        var words = question.Split([' ', ',', '?', '.', '!', ':', ';', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var run = new List<string>();
        void Flush()
        {
            if (run.Count >= 2 && run.Count <= 5) // iki kelimelik okul adının baş harfleri ("Örnek Üniversitesi" → "öü") çok kısa, alınmaz (başka okulun alan adı olabilir)
                if (TurkishText.Normalize(string.Concat(run.Select(w => w[0]))) is { Length: >= 3 } initials)
                    result.Add(initials);
            run.Clear();
        }
        foreach (var w in words)
        {
            if (char.IsUpper(w[0]) && w.Any(char.IsLetter))
            {
                run.Add(w);
                var n = TurkishText.Normalize(w.TrimEnd('\'', '’')).Split(' ')[0];
                if (n.Length >= 3 && !StopWords.Contains(n))
                    result.Add(n); // tek kelime de olabilir: "Hacettepe", "ODTÜ" → "odtu"
            }
            else
                Flush();
        }
        Flush();
        return result.Distinct().ToList();
    }

    /// <summary>Metni paragraf sınırlarından ~900 karakterlik parçalara böler (çok kısa satırlar — menü — birleşir).</summary>
    internal static IEnumerable<string> Split(string text)
    {
        var current = new System.Text.StringBuilder();
        var any = false;
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (current.Length > 0 && current.Length + line.Length > PassageChars)
            {
                if (current.Length > 120)
                {
                    any = true;
                    yield return current.ToString();
                }
                current.Clear();
            }
            current.Append(line.Length > PassageChars ? line[..PassageChars] : line).Append('\n');
        }
        if (current.Length > 120 || (!any && current.ToString().Trim().Length >= 10)) // kısa sayfa ya da arama özeti tek parça
            yield return current.ToString().TrimEnd();
    }

    private const string EnoughSchema = """{"type":"object","properties":{"yeterli":{"type":"boolean"},"eksik_sorgu":{"type":"string"}},"required":["yeterli","eksik_sorgu"]}""";

    private async Task<string?> MissingQueryAsync(string question, IReadOnlyList<Passage> passages, CancellationToken cancellationToken)
    {
        if (passages.Count == 0)
            return null;
        try
        {
            var digest = string.Join("\n", passages.Select((p, i) => $"[{i + 1}] {(p.Text.Length > 300 ? p.Text[..300] : p.Text)}"));
            var json = await chat.CompleteJsonAsync([
                new ChatMessage(Role.System, "Aşağıdaki kaynak parçaları soruyu tam cevaplamaya yetiyor mu? Yetmiyorsa eksik kalan kısmı bulmak için TEK, kısa bir web arama sorgusu yaz; yetiyorsa yeterli=true ve eksik_sorgu boş."),
                new ChatMessage(Role.User, $"Soru: {question}\n\nParçalar:\n{digest}"),
            ], EnoughSchema, cancellationToken);
            var node = JsonNode.Parse(json);
            var enough = node?["yeterli"]?.GetValue<bool>() ?? true;
            var more = node?["eksik_sorgu"]?.GetValue<string>()?.Trim();
            return enough || string.IsNullOrWhiteSpace(more) ? null : more;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Yeterlilik kontrolü yapılamadı");
            return null;
        }
    }

    private async Task<IReadOnlyList<WebResult>> SafeSearchAsync(string query, IReadOnlyList<string> domains, CancellationToken cancellationToken)
    {
        try
        {
            return domains.Count > 0 ? await search.SearchAsync(query, domains, cancellationToken) : await search.SearchAsync(query, cancellationToken);
        }
        catch (Exception ex) // süre bütçesi dolduysa da: sonuçsuz devam
        {
            logger.LogInformation(ex, "Arama yapılamadı: {Query}", query);
            return [];
        }
    }
}
