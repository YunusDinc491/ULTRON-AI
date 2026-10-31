using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Ultron.Core.Documents;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;
using Ultron.Core.Text;
using Ultron.Core.Web;

namespace Ultron.Core.Sites;

/// <summary>
/// "Bildiğim siteler" (Faz 20, kullanıcı: akademik takvim okulun sitesinde açıkça vardı ama Ultron bulamadı). Site bir kez söylenir;
/// Ultron onu her gece tarar (aynı alan adı, PDF'ler dahil, öncelik "takvim, duyuru, sınav" gibi amaca uyan bağlantılarda),
/// sayfaları parçalayıp kendi kopyasına yazar. Soru gelince internete çıkmadan bu kopyadan cevaplar.
/// Benzetme: okulun panosunun her sabah çekilmiş fotokopisi cepte.
/// </summary>
public sealed class SiteService(
    ISiteStore store,
    IWebPageReader reader,
    IEmbeddingModel embedder,
    TimeProvider time,
    ILogger<SiteService> logger,
    SchoolAssistant? school = null)
{
    public const int PageChars = 60_000, MaxDepth = 3;

    /// <summary>Soru–parça benzerliği bundan yüksekse cevap sitenin kopyasından verilir (2026-10-29 ölçümüyle ayarlandı).</summary>
    public const double StrongScore = 0.62;

    /// <summary>Soru siteyi işaret ediyorsa ("okulumun", sitenin adı) yeterli benzerlik.</summary>
    public const double MentionedScore = 0.45;

    public static readonly TimeOnly NightlyAt = new(4, 30);

    private static readonly string[] DefaultPriority =
        ["takvim", "akademik", "duyuru", "sinav", "ders", "program", "yonetmelik", "tarih", "kayit", "burc", "gunluk", "yorum", "haftalik"];

    private static readonly string[] SchoolWords = ["okul", "universite", "fakulte", "bolum", "akademik", "donem", "sinav takvim", "ders kayit", "yemekhane"];

    private readonly Channel<long> _requests = Channel.CreateUnbounded<long>();
    private readonly SemaphoreSlim _crawlGate = new(1, 1);
    private (long[] PageIds, List<(SiteChunk Chunk, SitePage Page)> Chunks)? _index;

    /// <summary>Taraması istenen siteler (eklenince ya da "şimdi tara" denince); zamanlayıcı okur.</summary>
    public ChannelReader<long> Requests => _requests.Reader;

    public Task<IReadOnlyList<KnownSite>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);

    /// <returns>Eklenen site ya da hata mesajı.</returns>
    public async Task<(KnownSite? Site, string Message)> AddAsync(string url, string? name, string? purpose, CancellationToken cancellationToken = default)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
            return (null, $"'{url}' geçerli bir web adresi değil.");
        var sites = await store.ListAsync(cancellationToken);
        if (sites.FirstOrDefault(s => BaseHost(new Uri(s.Url)) == BaseHost(uri)) is { } existing)
            return (existing, $"{existing.Name} zaten takip ediliyor.");
        var site = await store.AddAsync(new KnownSite
        {
            Name = string.IsNullOrWhiteSpace(name) ? BaseHost(uri) : $"{name.Trim()} ({BaseHost(uri)})",
            Url = uri.AbsoluteUri,
            Purpose = purpose?.Trim() ?? "",
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        _requests.Writer.TryWrite(site.Id);
        return (site, $"{site.Name} takibe alındı; şimdi taranıyor (birkaç dakika sürebilir), sonra her gece {NightlyAt:HH\\:mm}'da yenilenecek.");
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        var removed = await store.RemoveAsync(id, cancellationToken);
        _index = null;
        return removed;
    }

    public bool RequestCrawl(long id) => _requests.Writer.TryWrite(id);

    /// <summary>Gece taraması zamanı geldi mi (bugün 04:30'dan sonra ve bugün henüz taranmadı).</summary>
    public IReadOnlyList<KnownSite> DueSites(IReadOnlyList<KnownSite> sites)
    {
        var now = time.GetLocalNow().DateTime;
        if (TimeOnly.FromDateTime(now) < NightlyAt)
            return [];
        return sites.Where(s => s.IsActive && (s.LastCrawledUtc is null || s.LastCrawledUtc.Value.ToLocalTime().Date < now.Date)).ToList();
    }

    /// <summary>Siteyi baştan tarar: yeni/değişen sayfalar parçalanıp vektörlenir, kaybolanlar silinir.</summary>
    public async Task<string> CrawlAsync(KnownSite site, CancellationToken cancellationToken = default)
    {
        await _crawlGate.WaitAsync(cancellationToken);
        try
        {
            var result = await CrawlCoreAsync(site, cancellationToken);
            site.LastCrawledUtc = time.GetUtcNow().UtcDateTime;
            site.LastResult = result;
            site.PageCount = (await store.PagesAsync(site.Id, cancellationToken)).Count;
            await store.UpdateAsync(site, cancellationToken);
            _index = null;
            logger.LogInformation("Site tarandı — {Site}: {Result}", site.Name, result);
            return result;
        }
        finally
        {
            _crawlGate.Release();
        }
    }

    private async Task<string> CrawlCoreAsync(KnownSite site, CancellationToken cancellationToken)
    {
        var start = new Uri(site.Url);
        var baseHost = BaseHost(start);
        var disallowed = await RobotsAsync(start, cancellationToken);
        var priority = DefaultPriority.Concat(Words(site.Purpose)).Distinct().ToArray();
        var existing = (await store.PagesAsync(site.Id, cancellationToken)).ToDictionary(p => p.Url, StringComparer.OrdinalIgnoreCase);

        // Öncelik kuyruğu: amaca uyan bağlantılar (yazısında/adresinde "takvim" geçen) ve PDF'ler önce, derinlik arttıkça geri.
        var queue = new PriorityQueue<(string Url, int Depth), double>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start.AbsoluteUri };
        queue.Enqueue((start.AbsoluteUri, 0), 0);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int read = 0, changed = 0, pdfs = 0, failed = 0;
        // Okul asistanı (Faz 24): ilk taramadan sonra eklenen sayfalar "yeni" sayılır.
        var known = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstCrawl = site.LastCrawledUtc is null;
        var newPages = new List<SitePage>();

        while (queue.TryDequeue(out var item, out _) && read < site.MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.ReadPageAsync(item.Url, PageChars, cancellationToken);
            await Task.Delay(300, cancellationToken); // siteyi yormamak için
            if (page is null || page.Text.Length < 80)
            {
                failed++;
                continue;
            }
            read++;
            visited.Add(page.Url);
            visited.Add(item.Url);
            if (page.IsPdf)
                pdfs++;
            if (await SaveIfChangedAsync(site, page, existing, cancellationToken))
            {
                changed++;
                if (!firstCrawl && !known.Contains(page.Url) && existing.TryGetValue(page.Url, out var added))
                    newPages.Add(added);
            }
            // Takvim PDF'i her taramada okunur (hatırlatmalar tarih başına bir kez kurulur).
            if (school is not null && page.IsPdf)
                await SafeSchoolAsync(() => school.OnPageAsync(page.Url, page.Text.Split('\n', 2)[0], page.Text, cancellationToken));

            if (item.Depth >= MaxDepth)
                continue;
            foreach (var link in page.Links)
            {
                if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var target) || !SameSite(target, baseHost)
                    || disallowed.Any(d => target.AbsolutePath.StartsWith(d, StringComparison.OrdinalIgnoreCase)) || !seen.Add(target.AbsoluteUri))
                    continue;
                queue.Enqueue((target.AbsoluteUri, item.Depth + 1), item.Depth + 1 - LinkScore(link, priority));
            }
        }

        // Tarama sınıra takılmadan bittiyse sitede artık olmayan sayfalar silinir.
        if (school is not null && newPages.Count > 0)
            await SafeSchoolAsync(() => school.OnNewPagesAsync(site, newPages, cancellationToken));

        var gone = read < site.MaxPages
            ? existing.Values.Where(p => !visited.Contains(p.Url)).Select(p => p.Id).ToList()
            : [];
        if (gone.Count > 0)
            await store.RemovePagesAsync(gone, cancellationToken);
        return read == 0
            ? "Hiç sayfa okunamadı (site kapalı ya da erişimi engelliyor olabilir)."
            : $"{read} sayfa okundu ({pdfs} PDF), {changed} yeni/değişen, {gone.Count} kaldırıldı, {failed} okunamadı";
    }

    private async Task SafeSchoolAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Okul asistanı adımı başarısız");
        }
    }

    /// <summary>Bağlantının amaca uygunluğu (0-3): yazısında/adresinde öncelikli kelime, PDF.</summary>
    internal static double LinkScore(WebLink link, IReadOnlyCollection<string> priority)
    {
        var text = TurkishText.Normalize(link.Text + " " + Uri.UnescapeDataString(link.Url).Replace('-', ' ').Replace('_', ' ').Replace('/', ' '));
        var score = priority.Count(w => text.Contains(w, StringComparison.Ordinal)) * 1.0;
        if (link.Url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            score += 0.5;
        return Math.Min(score, 3);
    }

    private async Task<bool> SaveIfChangedAsync(KnownSite site, WebPage page, Dictionary<string, SitePage> existing, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text)));
        if (existing.TryGetValue(page.Url, out var old) && old.ContentHash == hash)
            return false;
        var parts = TextChunker.Split([new DocumentPage(null, page.Text)]);
        if (parts.Count == 0)
            return false;
        var vectors = new List<float[]>(parts.Count);
        foreach (var batch in parts.Chunk(16))
            vectors.AddRange(await embedder.EmbedAsync(batch.Select(p => p.Text).ToList(), EmbeddingPurpose.Document, cancellationToken));
        var title = page.Text.Split('\n', 2)[0].Trim();
        var saved = new SitePage
        {
            Id = old?.Id ?? 0,
            SiteId = site.Id,
            Url = page.Url,
            Title = title.Length is > 0 and <= 160 ? title : Uri.UnescapeDataString(new Uri(page.Url).AbsolutePath),
            ContentHash = hash,
            IsPdf = page.IsPdf,
            UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        await store.SavePageAsync(saved, parts.Select((p, i) => new SiteChunk { Ordinal = i, Text = p.Text, Embedding = vectors[i] }).ToList(), cancellationToken);
        existing[page.Url] = saved;
        return true;
    }

    /// <summary>robots.txt'teki "User-agent: *" yasakları (basit okuma; sitenin istemediği yerler taranmaz).</summary>
    private async Task<IReadOnlyList<string>> RobotsAsync(Uri start, CancellationToken cancellationToken)
    {
        var page = await reader.ReadPageAsync(new Uri(start, "/robots.txt").AbsoluteUri, 20_000, cancellationToken);
        return page is null ? [] : ParseRobots(page.Text);
    }

    internal static IReadOnlyList<string> ParseRobots(string robots)
    {
        var rules = new List<string>();
        var applies = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
                applies = value == "*";
            else if (applies && key == "disallow" && value.Length > 0 && !value.Contains('*'))
                rules.Add(value);
        }
        return rules;
    }

    // ---- Arama ----

    /// <summary>Etkin sitelerin kopyasında anlam araması.</summary>
    public async Task<IReadOnlyList<SiteHit>> SearchAsync(string query, int limit = 6, CancellationToken cancellationToken = default)
    {
        var sites = (await store.ListAsync(cancellationToken)).Where(s => s.IsActive).ToDictionary(s => s.Id);
        if (sites.Count == 0 || string.IsNullOrWhiteSpace(query))
            return [];
        var index = _index ??= ([], (await store.ChunksAsync(cancellationToken)).ToList());
        if (index.Chunks.Count == 0)
            return [];
        var q = (await embedder.EmbedAsync([query], EmbeddingPurpose.Query, cancellationToken))[0];
        var words = Words(query);
        var ranked = index.Chunks
            .Where(c => c.Chunk.Embedding.Length == q.Length && sites.ContainsKey(c.Page.SiteId))
            .Select(c =>
            {
                // Anlam benzerliği + başlıkta/adreste sorunun kelimesi geçiyorsa küçük bir artı ("akademik takvim" PDF'i).
                var title = TurkishText.Normalize(c.Page.Title + " " + Uri.UnescapeDataString(c.Page.Url));
                var bonus = words.Count(w => title.Contains(w, StringComparison.Ordinal)) * 0.02;
                return new SiteHit(c.Chunk, c.Page, sites[c.Page.SiteId], VectorMath.Cosine(q, c.Chunk.Embedding) + Math.Min(bonus, 0.06));
            })
            .OrderByDescending(h => h.Score)
            .ToList();
        if (ranked.Count == 0)
            return [];
        // En iyi sayfa kısa bir belgeyse (takvim PDF'i ~10 parça) tamamı verilir: tablonun "final" satırı başka parçada kalabiliyordu
        // (Faz 20 ölçümü: başlangıç tarihi geldi, final tarihi gelmedi). Diğer sayfalardan en fazla 2'şer parça.
        var best = ranked[0];
        var bestPage = ranked.Where(h => h.Page.Id == best.Page.Id).ToList();
        var top = bestPage.Count <= 14 ? bestPage.OrderBy(h => h.Chunk.Ordinal).Select(h => h with { Score = best.Score }).ToList() : bestPage.Take(4).ToList();
        var others = ranked.Where(h => h.Page.Id != best.Page.Id).GroupBy(h => h.Page.Id).SelectMany(g => g.Take(2)).Take(Math.Max(0, limit - 2));
        return [.. top, .. others];
    }

    /// <summary>Soru takip edilen bir siteyi işaret ediyor mu ("okulun", "üniversitenin", sitenin adı)? Kişisel soru sanılsa da siteye bakılır.</summary>
    public static bool Mentions(string text, IReadOnlyList<KnownSite> sites)
    {
        var t = " " + TurkishText.Normalize(text) + " ";
        return sites.Any(s => s.IsActive && (Words(s.Name).Any(w => w.Length >= 4 && t.Contains(" " + w, StringComparison.Ordinal))
            || (SchoolWords.Any(w => t.Contains(w, StringComparison.Ordinal)) && Words(s.Purpose + " " + s.Name).Concat(SchoolWords).Any(w => t.Contains(w, StringComparison.Ordinal)))));
    }

    /// <summary>Web aramasını daraltmak için sorunun işaret ettiği sitelerin alan adları.</summary>
    public async Task<IReadOnlyList<string>> DomainsForAsync(string text, CancellationToken cancellationToken = default)
    {
        var sites = (await store.ListAsync(cancellationToken)).Where(s => s.IsActive).ToList();
        return Mentions(text, sites) ? sites.Select(s => BaseHost(new Uri(s.Url))).Distinct().ToList() : [];
    }

    /// <summary>Cevap için not: sitenin kopyasından parçalar, sayfa başına bir [n] numarası.</summary>
    public static string Note(IReadOnlyList<SiteHit> hits)
    {
        var sb = new StringBuilder("Aşağıdaki bilgiler Ultron'un takip ettiği sitelerin kendi kopyasından (her gece yenilenir). Cevabı ÖNCELİKLE bunlara dayandır, [n] ile kaynak göster; tarihleri ve sayıları aynen aktar, sorulan her şeyi (ör. hem başlangıç hem final tarihleri) belgede ara:\n");
        foreach (var (page, i) in ByPage(hits).Select((p, i) => (p, i)))
            sb.AppendLine($"[{i + 1}] {page[0].Site.Name} · {page[0].Page.Title} — {page[0].Page.Url} (güncellendi {page[0].Page.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy})\n{string.Join("\n", page.Select(h => h.Chunk.Text))}\n");
        return sb.ToString().TrimEnd();
    }

    public static IReadOnlyList<WebSource> Sources(IReadOnlyList<SiteHit> hits) =>
        ByPage(hits).Select((p, i) => new WebSource(i + 1, $"{p[0].Site.Name} · {p[0].Page.Title}", p[0].Page.Url, string.Join("\n", p.Select(h => h.Chunk.Text)))).ToList();

    /// <summary>Parçalar sayfa sayfa (ilk görülme sırasıyla), sayfa içinde belge sırasıyla.</summary>
    private static List<List<SiteHit>> ByPage(IReadOnlyList<SiteHit> hits) =>
        hits.GroupBy(h => h.Page.Id).Select(g => g.OrderBy(h => h.Chunk.Ordinal).ToList()).ToList();

    internal static string BaseHost(Uri uri) => uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..].ToLowerInvariant() : uri.Host.ToLowerInvariant();

    /// <summary>Aynı site: aynı alan adı ya da alt alan adı (oidb.okul.edu.tr, okul.edu.tr'nin parçası).</summary>
    internal static bool SameSite(Uri target, string baseHost)
    {
        if (target.Scheme is not ("http" or "https"))
            return false;
        var host = BaseHost(target);
        return host == baseHost || host.EndsWith("." + baseHost, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> Words(string text) =>
        TurkishText.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).Distinct().ToList();
}
