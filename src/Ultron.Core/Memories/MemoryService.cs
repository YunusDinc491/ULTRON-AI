using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Embeddings;
using Ultron.Core.Text;

namespace Ultron.Core.Memories;

/// <summary>Hafızanın beyni: yeni bilgiyi kaydeder/günceller (çelişki yönetimi) ve soruya göre alakalı bilgiyi bulur.</summary>
public sealed class MemoryService(
    MemoryExtractor extractor,
    IChatModel chat,
    IEmbeddingModel embedder,
    IMemoryStore store,
    ICategoryStore categories,
    IOptions<MemoryOptions> options,
    ILogger<MemoryService> logger,
    ICategoryNotifier? categoryNotifier = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private MemoryOptions Options => options.Value;

    /// <summary>Kullanıcı mesajından bilgi çıkarır ve her birini hafızayla uzlaştırır.</summary>
    /// <param name="previousAssistant">Ultron'un bir önceki mesajı ("Evet" gibi kısa cevapların bağlamı).</param>
    public async Task ProcessAsync(string userText, long? sourceMessageId, CancellationToken cancellationToken = default, string? previousAssistant = null)
    {
        var extracted = await extractor.ExtractAsync(userText, cancellationToken, previousAssistant);
        await SaveAsync(extracted.Facts, sourceMessageId, cancellationToken);
    }

    /// <summary>
    /// Yeni klasör isteyen bilgiyi yerleştirir. Küçük model aynı konuya farklı adlar verebiliyor ("Kedi", "Evcil hayvan"), bu yüzden
    /// önce aynı çekmecede benzer klasör aranır: ad aynıysa ya da biri ötekinin başıysa ("Aile"/"Ailesi") o; anlam benzerliği yüksekse o;
    /// arada kalırsa modele "aynı konu mu?" diye sorulur. Hiçbiri değilse Ultron klasörü açar ve haber verir (tasarım kararı, 2026-10-27).
    /// </summary>
    private async Task<string> PlaceAsync(ExtractedFact fact, CancellationToken cancellationToken)
    {
        if (fact.New is not { } proposed)
            return fact.Category;

        var inDrawer = (await categories.GetAllAsync(cancellationToken)).Where(c => c.Drawer == fact.Drawer).ToList();
        var name = TurkishText.Normalize(proposed.Name);
        if (inDrawer.FirstOrDefault(c => SameName(TurkishText.Normalize(c.Name), name)) is { } byName)
            return byName.Key;

        if (inDrawer.Count > 0)
        {
            var vectors = await embedder.EmbedAsync([Label(proposed.Name, proposed.Description), .. inDrawer.Select(c => Label(c.Name, c.Description))], EmbeddingPurpose.Document, cancellationToken);
            var (best, score) = inDrawer.Select((c, i) => (c, VectorMath.Cosine(vectors[0], vectors[i + 1]))).MaxBy(x => x.Item2);
            if (score >= Options.CategoryMergeMinScore)
                return Merged(best, score);
            if (score >= Options.CategoryAskMinScore)
            {
                try
                {
                    if (await extractor.SameTopicAsync(fact.Drawer, proposed, best, cancellationToken))
                        return Merged(best, score);
                }
                catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
                {
                    logger.LogDebug(ex, "Klasör karşılaştırması yapılamadı; yeni klasör açılıyor");
                }
            }
        }

        var category = await categories.AddAsync(proposed.Name, proposed.Description, fact.Drawer, cancellationToken);
        logger.LogInformation("Yeni kategori açıldı: {Drawer} › {Name} ({Example})", fact.Drawer, category.Name, fact.Content);
        if (categoryNotifier is not null)
            await categoryNotifier.NotifyCategoryCreatedAsync(category, fact.Content, cancellationToken);
        return category.Key;

        string Merged(MemoryCategory c, double s)
        {
            logger.LogInformation("Yeni klasör '{New}' mevcut '{Existing}' ile aynı sayıldı ({Score:F2})", proposed.Name, c.Name, s);
            return c.Key;
        }
    }

    private static string Label(string name, string description) => $"{name}: {description}";

    /// <summary>Aynı ad ya da biri ötekinin başı ("aile" / "ailesi"); kısa adlarda ("ev" / "evcil") sadece tam eşitlik.</summary>
    internal static bool SameName(string a, string b) =>
        a == b || (Math.Min(a.Length, b.Length) >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)));

    /// <summary>Web/araştırma cevabından öğrenilenleri (en fazla 3) BİLGİ çekmecesine kaydeder.</summary>
    public async Task ProcessLearnedAsync(string question, string answer, long? sourceMessageId, CancellationToken cancellationToken = default)
    {
        var facts = await extractor.ExtractLearnedAsync(question, answer, cancellationToken);
        if (facts.Count > 0)
            logger.LogInformation("Araştırmadan öğrenildi: {Facts}", string.Join(" | ", facts.Select(f => f.Content)));
        await SaveAsync(facts, sourceMessageId, cancellationToken);
    }

    private async Task SaveAsync(IReadOnlyList<ExtractedFact> extracted, long? sourceMessageId, CancellationToken cancellationToken)
    {
        if (extracted.Count == 0)
            return;

        // Sırayla: aynı mesajda aynı yeni klasörü isteyen ikinci bilgi, ilkinin açtığını bulsun.
        var facts = new List<ExtractedFact>(extracted.Count);
        foreach (var fact in extracted)
            facts.Add(fact with { Category = await PlaceAsync(fact, cancellationToken), New = null });

        var vectors = await embedder.EmbedAsync(facts.Select(f => f.Content).ToList(), EmbeddingPurpose.Document, cancellationToken);
        for (var i = 0; i < facts.Count; i++)
            await ConsolidateAsync(facts[i], vectors[i], sourceMessageId, cancellationToken);
    }

    /// <summary>
    /// Üç kaynaktan birleşik hatırlama:
    /// 1) çekirdek profil (her zaman), 2) anlamca benzer kayıtlar (embedding), 3) anahtar kelime eşleşmeleri (FTS5).
    /// Aynı kayıt birden fazla kaynaktan gelirse bir kez yer alır.
    /// </summary>
    public async Task<MemoryRecall> RecallAsync(string query, CancellationToken cancellationToken = default)
    {
        var active = await store.GetActiveAsync(cancellationToken);
        if (active.Count == 0)
            return MemoryRecall.Empty;

        var coreKeys = (await categories.GetAllAsync(cancellationToken))
            .Where(c => c.IsCore)
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var core = active
            .Where(m => coreKeys.Contains(m.Category))
            .OrderByDescending(m => m.UpdatedAtUtc)
            .Take(Options.CoreMaxItems)
            .ToList();

        var queryVector = (await embedder.EmbedAsync([query], EmbeddingPurpose.Query, cancellationToken))[0];
        var ranked = Rank(queryVector, active);
        foreach (var (memory, score) in ranked.Take(Options.RecallTopK))
            logger.LogDebug("Anlamsal aday {Score:F2}: {Content}", score, memory.Content);

        var semantic = ranked
            .Where(x => x.Score >= Options.RecallMinScore)
            .Take(Options.RecallTopK)
            .Select(x => x.Memory);

        var prefixes = KeywordQuery.Prefixes(query);
        var keyword = prefixes.Count == 0
            ? []
            : await store.SearchKeywordsAsync(prefixes, Options.KeywordTopK, cancellationToken);
        foreach (var memory in keyword)
            logger.LogDebug("Anahtar kelime eşleşmesi [{Prefixes}]: {Content}", string.Join(",", prefixes), memory.Content);

        var coreIds = core.Select(m => m.Id).ToHashSet();
        var relevant = semantic
            .Concat(keyword)
            .Where(m => !coreIds.Contains(m.Id))
            .DistinctBy(m => m.Id)
            .ToList();

        return new MemoryRecall(core, relevant);
    }

    // ---- Hafıza panelinden elle düzenleme. Model yanlış bilgi çıkarabilir: kullanıcı her zaman düzeltebilmeli. ----

    /// <summary>Kullanıcının elle eklediği bilgi: çelişki kontrolü yapılmaz, kullanıcı ne yazdıysa o.</summary>
    public async Task<Memory> AddManualAsync(string content, string category, CancellationToken cancellationToken = default)
    {
        content = content.Trim();
        var vector = (await embedder.EmbedAsync([content], EmbeddingPurpose.Document, cancellationToken))[0];
        var added = await store.AddAsync(new Memory { Category = category, Content = content, Embedding = vector }, cancellationToken);
        logger.LogInformation("Elle eklendi #{Id} [{Category}]: '{Content}'", added.Id, category, content);
        return added;
    }

    /// <summary>Metin değişince anlam vektörü de yeniden üretilir; yoksa arama eski metne göre bulur.</summary>
    public async Task UpdateManualAsync(long id, string content, string category, CancellationToken cancellationToken = default)
    {
        content = content.Trim();
        var vector = (await embedder.EmbedAsync([content], EmbeddingPurpose.Document, cancellationToken))[0];
        await store.UpdateAsync(id, content, category, vector, cancellationToken);
        logger.LogInformation("Elle düzenlendi #{Id} [{Category}]: '{Content}'", id, category, content);
    }

    private async Task ConsolidateAsync(ExtractedFact fact, float[] vector, long? sourceMessageId, CancellationToken cancellationToken)
    {
        var active = await store.GetActiveAsync(cancellationToken);
        var candidates = Rank(vector, active)
            .Where(x => x.Score >= Options.ConflictCandidateMinScore)
            .Take(3)
            .Select(x => x.Memory)
            .ToList();

        // Benzer kayıt yoksa modele sormaya gerek yok: doğrudan ekle.
        var decision = candidates.Count == 0
            ? new MemoryDecision("EKLE", null, null)
            : await DecideAsync(fact, candidates, cancellationToken);

        var target = candidates.FirstOrDefault(c => c.Id == decision.Id);
        switch (decision.Islem.ToUpperInvariant())
        {
            case "GUNCELLE" when target is not null:
                var content = string.IsNullOrWhiteSpace(decision.Icerik) ? fact.Content : decision.Icerik.Trim();
                var newVector = content == fact.Content ? vector : (await embedder.EmbedAsync([content], EmbeddingPurpose.Document, cancellationToken))[0];
                await store.UpdateAsync(target.Id, content, fact.Category, newVector, cancellationToken);
                logger.LogInformation("Hafıza güncellendi #{Id}: '{Old}' → '{New}'", target.Id, target.Content, content);
                break;

            case "SIL" when target is not null:
                await store.DeactivateAsync(target.Id, cancellationToken);
                logger.LogInformation("Hafıza pasifleştirildi #{Id}: '{Old}' (yeni bilgi: '{New}')", target.Id, target.Content, fact.Content);
                break;

            case "YOK":
                logger.LogInformation("Zaten biliniyor: '{Content}'", fact.Content);
                break;

            default: // EKLE, ya da model geçersiz bir id verdiyse güvenli taraf: ekle
                var added = await store.AddAsync(new Memory
                {
                    Category = fact.Category,
                    Content = fact.Content,
                    Embedding = vector,
                    SourceMessageId = sourceMessageId,
                }, cancellationToken);
                logger.LogInformation("Hafızaya eklendi #{Id} [{Category}]: '{Content}'", added.Id, fact.Category, fact.Content);
                break;
        }
    }

    private async Task<MemoryDecision> DecideAsync(ExtractedFact fact, IReadOnlyList<Memory> candidates, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder("MEVCUT kayıtlar:\n");
        foreach (var c in candidates)
            prompt.AppendLine($"- id {c.Id} [{c.Category}]: {c.Content}");
        prompt.AppendLine($"\nYENİ bilgi [{fact.Category}]: {fact.Content}");

        var json = await chat.CompleteJsonAsync(
            [new(Role.System, MemoryPrompts.Decision), new(Role.User, prompt.ToString())],
            MemoryPrompts.DecisionSchema,
            cancellationToken);

        return JsonSerializer.Deserialize<MemoryDecision>(json, JsonOptions) ?? new MemoryDecision("EKLE", null, null);
    }

    private static List<(Memory Memory, double Score)> Rank(float[] vector, IEnumerable<Memory> memories) =>
        memories
            .Where(m => m.Embedding.Length == vector.Length)
            .Select(m => (m, VectorMath.Cosine(vector, m.Embedding)))
            .OrderByDescending(x => x.Item2)
            .ToList();

    private sealed record MemoryDecision(string Islem, long? Id, string? Icerik);
}
