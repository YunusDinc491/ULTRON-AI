using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Ultron.Core.Embeddings;
using Ultron.Core.Settings;
using Ultron.Core.Text;

namespace Ultron.Core.Library;

/// <summary>
/// Yazılım Kütüphanesi (Faz 15 a). Kütüphaneci gibi: rafları (kaynaklar) haftada bir yeniler — sadece değişen kitapları (sayfaları)
/// yeniden fişler —, soru gelince hem fiş kataloğundan (FTS5, kelimeyle) hem anlam benzerliğinden (vektör) arar ve ikisini birleştirir.
/// Vektörler bir kez belleğe alınır (her soruda veritabanından yüzbinlerce vektör okunmasın); güncellemeden sonra yenilenir.
/// Kişisel hafızadan ve kişisel belgelerden tamamen ayrı tutulur.
/// </summary>
public sealed partial class LibraryService(
    ILibraryStore store,
    ILibraryFetcher fetcher,
    IEmbeddingModel embedder,
    UltronSettings settings,
    TimeProvider time,
    ILogger<LibraryService> logger)
{
    public const string LastSyncKey = "kutuphane_son_guncelleme";

    /// <summary>Kaynağın en son eksiksiz işlendiği commit (iç ayar).</summary>
    public static string SourceDoneKey(string sourceId) => "kutuphane_tamam_" + sourceId;

    /// <summary>Bellek dizini için vektörün ilk bu kadar boyutu (Qwen3-Embedding Matryoshka destekler: 1024'ün ilk 256'sı, 4 kat az bellek).</summary>
    public const int IndexDimensions = 256;

    /// <summary>Çok uzun sayfalar (ör. dev API listeleri) kütüphaneyi şişirmesin: sayfa başına en fazla bu kadar parça.</summary>
    public const int MaxChunksPerPage = 80;

    /// <summary>Arama sonucunda aynı sayfadan en fazla bu kadar parça (çeşitlilik).</summary>
    public const int MaxChunksPerPageInResults = 2;

    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private (long[] Ids, float[] Vectors)? _index;

    /// <summary>Kaynak listesi (dosyadan; yoksa varsayılan). Infrastructure dosyadan okuyup buraya verir.</summary>
    public IReadOnlyList<LibrarySource> Sources { get; set; } = LibrarySources.Default;

    /// <summary>Şu anki güncelleme durumu ("mdn: 1.240/9.800 sayfa") ya da null.</summary>
    public string? Progress { get; private set; }

    public bool IsSyncing => Progress is not null;

    // ---- Güncelleme ----

    /// <summary>Bütün kaynakları sırayla günceller. Aynı anda ikinci bir güncelleme başlamaz.</summary>
    public async Task<string> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        if (!await _syncGate.WaitAsync(0, cancellationToken))
            return "Kütüphane zaten güncelleniyor: " + Progress;
        var report = new List<string>();
        try
        {
            foreach (var source in Sources)
            {
                try
                {
                    report.Add(await SyncSourceAsync(source, cancellationToken));
                    _index = null; // ilk indirme saatler sürer: biten kaynak hemen aranabilsin
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Kütüphane kaynağı güncellenemedi: {Source}", source.Id);
                    report.Add($"{source.Name}: HATA ({ex.Message})");
                }
            }
            await settings.SetInternalAsync(LastSyncKey, time.GetUtcNow().UtcDateTime.ToString("O"), cancellationToken);
            return string.Join("\n", report);
        }
        finally
        {
            Progress = null;
            _index = null; // yeni parçalar: dizin bir sonraki aramada yeniden yüklenir
            _syncGate.Release();
        }
    }

    internal async Task<string> SyncSourceAsync(LibrarySource source, CancellationToken cancellationToken)
    {
        Progress = $"{source.Name}: indiriliyor";
        var (folder, commit) = await fetcher.FetchAsync(source, cancellationToken);
        var existing = await store.GetPagesAsync(source.Id, cancellationToken);

        // Depo aynı commit'te ve kaynak o commit'le eksiksiz işlendiyse taramaya gerek yok (Ultron yeniden başlayınca 11 bin MDN
        // sayfası baştan okunuyordu). Embedding geçişi özetleri sildiyse (boş ContentHash) yine işlenir.
        var doneKey = SourceDoneKey(source.Id);
        if (existing.Count > 0 && await settings.GetInternalAsync(doneKey, cancellationToken) == commit && existing.Values.All(p => p.ContentHash.Length > 0))
            return $"{source.Name}: değişmedi · sürüm {commit[..Math.Min(7, commit.Length)]}";
        var files = source.Paths
            .Select(p => Path.Combine(folder, p.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            .Where(f => source.Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            // DocFX "includes" klasörleri sayfa değil, sayfalara gömülen (çoğu eski sürüm) parçalar: aramada sayfanın yerine çıkıyordu.
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Contains("includes", StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        var now = time.GetUtcNow().UtcDateTime;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unchanged = new List<long>();
        int added = 0, updated = 0, done = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = $"{source.Name}: {++done:N0}/{files.Count:N0} sayfa";
            var repoPath = Path.GetRelativePath(folder, file).Replace('\\', '/');
            var page = DocCleaner.Clean(await File.ReadAllTextAsync(file, cancellationToken), repoPath);
            if (page.Text.Length < 80)
                continue; // boş/yönlendirme sayfası
            seen.Add(repoPath);
            var hash = Sha256(page.Text);
            if (existing.TryGetValue(repoPath, out var old) && old.ContentHash == hash && !old.IsStale)
            {
                unchanged.Add(old.Id);
                continue;
            }

            var texts = DocCleaner.Chunk(page).Take(MaxChunksPerPage).ToList();
            if (texts.Count == 0)
                continue;
            var vectors = new List<float[]>(texts.Count);
            foreach (var batch in texts.Chunk(32))
                vectors.AddRange(await embedder.EmbedAsync(batch, EmbeddingPurpose.Document, cancellationToken));
            var record = new LibraryPage
            {
                Id = old?.Id ?? 0,
                SourceId = source.Id,
                Path = repoPath,
                Title = page.Title,
                Url = source.Repo.StartsWith(LibrarySources.LocalPrefix, StringComparison.Ordinal) ? new Uri(file).AbsoluteUri : LibrarySources.UrlFor(source, repoPath),
                Version = commit,
                ContentHash = hash,
                UpdatedAtUtc = now,
            };
            await store.SavePageAsync(record, texts.Select((t, i) => new LibraryChunk { Ordinal = i, Text = t, Embedding = vectors[i] }).ToList(), cancellationToken);
            if (old is null) added++; else updated++;
        }

        await store.TouchPagesAsync(unchanged, commit, now, cancellationToken);
        var gone = existing.Where(kv => !seen.Contains(kv.Key) && !kv.Value.IsStale).Select(kv => kv.Value.Id).ToList();
        await store.MarkStaleAsync(gone, cancellationToken);
        await settings.SetInternalAsync(doneKey, commit, cancellationToken); // yarıda kesilirse işaret yazılmaz, sonraki sefer baştan taranır
        var summary = $"{source.Name}: {seen.Count:N0} sayfa ({added} yeni, {updated} değişti, {gone.Count} kaldırıldı) · sürüm {commit[..Math.Min(7, commit.Length)]}";
        logger.LogInformation("Kütüphane güncellendi — {Summary}", summary);
        return summary;
    }

    /// <summary>Haftalık güncelleme anı: pazar 03:30 (tasarım kararı: haftalık). Bilgisayar o saatte kapalıysa açılışta yakalanır.</summary>
    public static readonly TimeOnly WeeklyAt = new(3, 30);

    /// <summary>En son geçen "pazar 03:30".</summary>
    public static DateTime LastSlot(DateTime localNow)
    {
        var daysSinceSunday = (int)localNow.DayOfWeek; // pazar = 0
        var slot = localNow.Date.AddDays(-daysSinceSunday).Add(WeeklyAt.ToTimeSpan());
        return slot > localNow ? slot.AddDays(-7) : slot;
    }

    /// <summary>Zamanlayıcı çağırır: hiç indirilmediyse ya da son pazar 03:30'dan beri güncellenmediyse günceller.</summary>
    public async Task<string?> TryRunScheduledAsync(CancellationToken cancellationToken = default)
    {
        var last = (await StatsAsync(cancellationToken)).LastSyncUtc;
        var localNow = time.GetLocalNow().DateTime;
        if (last is { } t && TimeZoneInfo.ConvertTimeFromUtc(t, time.LocalTimeZone) >= LastSlot(localNow))
            return null;
        return await SyncAllAsync(cancellationToken);
    }

    // ---- Arama ----

    /// <summary>Hibrit arama: kelime (FTS5) + anlam (vektör), sıralar karşılıklı sıra birleştirmesiyle (RRF) harmanlanır.</summary>
    public Task<IReadOnlyList<LibraryHit>> SearchAsync(string query, int limit = 6, CancellationToken cancellationToken = default) =>
        SearchAsync([query], limit, cancellationToken);

    /// <summary>
    /// Birden çok sorguyla (kullanıcının Türkçe sorusu + modelin yazdığı İngilizce arama ifadesi) arar; her sorgunun anlam ve kelime
    /// sıraları aynı RRF havuzunda toplanır. Belgeler İngilizce: "map ile forEach farkı" tek başına Map nesnesine kayıyordu.
    /// </summary>
    public async Task<IReadOnlyList<LibraryHit>> SearchAsync(IReadOnlyList<string> queries, int limit = 6, CancellationToken cancellationToken = default)
    {
        var index = await IndexAsync(cancellationToken);
        if (index.Ids.Length == 0 || queries.Count == 0)
            return [];

        var scores = new Dictionary<long, double>();
        void Add(IEnumerable<long> ranked)
        {
            var rank = 0;
            foreach (var id in ranked)
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (60 + ++rank);
        }

        var vectors = await embedder.EmbedAsync(queries, EmbeddingPurpose.Query, cancellationToken);
        for (var q = 0; q < queries.Count; q++)
        {
            Add(TopByVector(index, Truncate(vectors[q]), 30));
            if (Terms(queries[q]) is { Count: > 0 } terms)
                Add(await store.SearchKeywordsAsync(terms, 30, cancellationToken));
        }

        // Tam API adı ("Array.prototype.map", "Promise.allSettled", "useEffect") geçiyorsa o başlıklı sayfa iki kat ağırlıkla öne:
        // "map ile forEach" sorusunda İngilizce ifade doğruyken bile Map.prototype.forEach sayfası öne çıkıyordu.
        foreach (var name in queries.SelectMany(q => ApiName().Matches(q).Select(m => m.Value)).Where(n => !ProductNames.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
        {
            var titled = await store.SearchTitlesAsync(name, 3, cancellationToken);
            Add(titled);
            Add(titled);
        }

        // Aynı sayfadan en fazla 2 parça: tek sayfanın üç parçası başka sayfalara yer bırakmasın (MDN denemesinde görüldü).
        var best = scores.OrderByDescending(kv => kv.Value).Take(limit * 4).ToList();
        var rows = await store.GetChunksAsync(best.Select(b => b.Key).ToList(), cancellationToken);
        var byId = rows.ToDictionary(r => r.Chunk.Id);
        var names = Sources.ToDictionary(s => s.Id, s => s.Name);
        var perPage = new Dictionary<long, int>();
        var hits = new List<LibraryHit>();
        foreach (var (id, score) in best)
        {
            if (!byId.TryGetValue(id, out var r) || r.Page.IsStale || perPage.GetValueOrDefault(r.Page.Id) >= MaxChunksPerPageInResults)
                continue;
            perPage[r.Page.Id] = perPage.GetValueOrDefault(r.Page.Id) + 1;
            hits.Add(new LibraryHit(r.Chunk, r.Page, names.GetValueOrDefault(r.Page.SourceId, r.Page.SourceId), score));
            if (hits.Count == limit)
                break;
        }
        return hits;
    }

    public async Task<LibraryStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await store.GetStatsAsync(cancellationToken);
        var last = await settings.GetInternalAsync(LastSyncKey, cancellationToken);
        return stats with { LastSyncUtc = DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null };
    }

    /// <summary>/durum: "11 kaynak · 9.812 sayfa · 61.240 parça · son güncelleme 28.10 04:10" (güncelleniyorsa ilerleme).</summary>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var s = await StatsAsync(cancellationToken);
        var last = s.LastSyncUtc is { } t ? TimeZoneInfo.ConvertTimeFromUtc(t, time.LocalTimeZone).ToString("dd.MM HH:mm") : "henüz yok";
        var text = s.Pages == 0 ? "boş (ilk indirme bekleniyor)" : $"{s.Sources} kaynak · {s.Pages:N0} sayfa · {s.Chunks:N0} parça · son güncelleme {last}";
        return Progress is { } p ? $"{text} · ŞU AN: {p}" : text;
    }

    /// <summary>Vektörler dışarıda değişti (embedding modeli geçişi): dizin bir sonraki aramada yeniden yüklenir.</summary>
    public void ResetIndex() => _index = null;

    // ---- Yardımcılar ----

    private async Task<(long[] Ids, float[] Vectors)> IndexAsync(CancellationToken cancellationToken)
    {
        if (_index is { } ready)
            return ready;
        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_index is { } again)
                return again;
            var rows = await store.GetEmbeddingsAsync(cancellationToken);
            var ids = new long[rows.Count];
            var vectors = new float[rows.Count * IndexDimensions];
            for (var i = 0; i < rows.Count; i++)
            {
                ids[i] = rows[i].ChunkId;
                Truncate(rows[i].Embedding).CopyTo(vectors, i * IndexDimensions);
            }
            if (rows.Count == 0)
                return (ids, vectors); // boş dizin tutulmaz: kütüphane dolunca bir sonraki soruda okunur
            logger.LogInformation("Kütüphane dizini belleğe alındı: {Count:N0} parça, {Mb:N0} MB", rows.Count, vectors.Length * 4 / 1024 / 1024);
            return (_index = (ids, vectors)).Value;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>İlk <see cref="IndexDimensions"/> boyut, birim uzunluğa normalleştirilmiş (iç çarpım = kosinüs benzerliği).</summary>
    internal static float[] Truncate(float[] vector)
    {
        var n = Math.Min(IndexDimensions, vector.Length);
        var result = new float[IndexDimensions];
        double norm = 0;
        for (var i = 0; i < n; i++)
            norm += vector[i] * vector[i];
        var scale = norm > 0 ? (float)(1 / Math.Sqrt(norm)) : 0f;
        for (var i = 0; i < n; i++)
            result[i] = vector[i] * scale;
        return result;
    }

    internal static IEnumerable<long> TopByVector((long[] Ids, float[] Vectors) index, float[] query, int k)
    {
        var best = new PriorityQueue<long, float>();
        for (var i = 0; i < index.Ids.Length; i++)
        {
            var span = index.Vectors.AsSpan(i * IndexDimensions, IndexDimensions);
            var score = Dot(span, query);
            if (best.Count < k)
                best.Enqueue(index.Ids[i], score);
            else if (best.TryPeek(out _, out var lowest) && score > lowest)
                best.EnqueueDequeue(index.Ids[i], score);
        }
        var result = new List<(long Id, float Score)>();
        while (best.TryDequeue(out var id, out var score))
            result.Add((id, score));
        return result.OrderByDescending(r => r.Score).Select(r => r.Id);
    }

    /// <summary>SIMD iç çarpım (100 bin × 256 boyut ≈ 10-20 ms).</summary>
    internal static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var width = System.Numerics.Vector<float>.Count;
        var acc = System.Numerics.Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - width; i += width)
            acc += new System.Numerics.Vector<float>(a[i..]) * new System.Numerics.Vector<float>(b[i..]);
        var sum = System.Numerics.Vector.Sum(acc);
        for (; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static readonly HashSet<string> StopWords =
    [
        // Türkçe soru kelimeleri ve İngilizce yaygın kelimeler: her sayfada geçer, ayırt etmez.
        "nasil", "neden", "nedir", "niye", "icin", "ile", "bir", "bu", "mi", "mu", "ne", "kullanilir", "yapilir", "yaparim",
        "olur", "var", "yok", "gibi", "daha", "cok", "ama", "veya", "hangi", "farki", "arasindaki", "calisiyor", "calismiyor",
        "the", "and", "for", "with", "how", "what", "why", "use", "using", "does", "can", "from", "into", "this", "that",
    ];

    /// <summary>Kelime araması terimleri: kod adları bütün kelime olarak ("useeffect", "flexbox"), en fazla 8.</summary>
    internal static IReadOnlyList<string> Terms(string query) =>
        TurkishText.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct()
            .Take(8)
            .ToList();

    /// <summary>Ürün adları API adı sayılmaz ("Next.js" başlıklı onlarca sayfa var).</summary>
    private static readonly HashSet<string> ProductNames = new(["Next.js", "Node.js", "ASP.NET", "React.js", "Vue.js", "Express.js", "Nuxt.js", "Three.js", "D3.js", "EF.Core"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Noktalı API adı (Array.prototype.map, Promise.allSettled, fs.readFile) ya da React kancası (useEffect).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\b[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+|\buse[A-Z]\w+")]
    private static partial System.Text.RegularExpressions.Regex ApiName();

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
