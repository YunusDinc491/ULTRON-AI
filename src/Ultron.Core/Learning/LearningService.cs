using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Coding;
using Ultron.Core.Embeddings;
using Ultron.Core.Settings;

namespace Ultron.Core.Learning;

/// <summary>appsettings "Learning" bölümü.</summary>
public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    /// <summary>Denetim çalışma klasörü: her gece bir alt klasör (girdi.json, rapor.md).</summary>
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "denetim");

    /// <summary>Claude Code'un salt okuyacağı kaynaklar: indirilen resmi dokümantasyon.</summary>
    public string[] ReferenceFolders { get; set; } =
        [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "kutuphane", "kaynaklar")];

    /// <summary>Bir gecede en fazla kaç cevap denetlenir (fazlası sonraki geceye kalır).</summary>
    public int MaxPerAudit { get; set; } = 40;

    /// <summary>
    /// Onaylı düzeltme bu benzerlikten itibaren yeni soruya bağlam olarak girer (soru-soru, qwen3-embedding, Query öneki).
    /// Ölçüm (2026-10-28, 10 çift): aynı konu 0,73-0,87, farklı konu 0,36-0,66 ("EF Core indeks" ↔ "EF Core migration" 0,66).
    /// </summary>
    public double ApplyMinScore { get; set; } = 0.70;

    /// <summary>Reddedilmiş bir öneriye bu kadar benzeyen soru için yeni öneri gösterilmez (aynı konu, daha sıkı).</summary>
    public double RejectedMinScore { get; set; } = 0.78;

    /// <summary>
    /// Resmi dokümantasyon adresleri: düzeltme bunlardan birine (ya da <see cref="ReferenceFolders"/> içindeki bir dosyaya)
    /// dayanıyorsa kendiliğinden onaylanır. github.com bilerek yok (herkesin deposu olabilir).
    /// </summary>
    public string[] OfficialHosts { get; set; } =
    [
        "developer.mozilla.org", "typescriptlang.org", "react.dev", "nextjs.org", "nodejs.org", "git-scm.com", "reactnative.dev",
        "flutter.dev", "dart.dev", "kotlinlang.org", "developer.android.com", "swift.org", "developer.apple.com", "learn.microsoft.com",
    ];
}

/// <summary>
/// Gece denetimi ve öğrenme (Faz 15 d). Benzetme: Ultron gün içinde ödev yapar, gece öğretmen (Claude Code) kâğıtları okur,
/// yanlışların yanına doğrusunu yazar; sabah kullanıcı hangi düzeltmelerin deftere geçeceğine karar verir.
/// Gizlilik: Claude Code'a sadece yazılım soruları ve Ultron'un cevapları gider; kişisel hafıza gitmez.
/// </summary>
public sealed partial class LearningService(
    IAnswerReviewStore store,
    IEmbeddingModel embedder,
    ICodeAgent agent,
    ILearningNotifier notifier,
    UltronSettings settings,
    IOptions<LearningOptions> options,
    TimeProvider time,
    ILogger<LearningService> logger)
{
    public const string LastAuditKey = "denetim_son";

    /// <summary>Her gece 04:00 (tasarım kararı). Bilgisayar kapalıysa açılışta yakalanır.</summary>
    public static readonly TimeOnly DailyAt = new(4, 0);

    private readonly SemaphoreSlim _auditGate = new(1, 1);

    public bool IsAuditing => _auditGate.CurrentCount == 0;

    // ---- Gün içi: cevapları kaydet, onaylı düzeltmeleri kullan ----

    /// <summary>Yazılım sorusunun cevabını denetime kaydeder. Hata cevabı engellemesin diye yutulur.</summary>
    public async Task RecordAsync(string question, string answer, string sources, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            return;
        try
        {
            var vector = (await embedder.EmbedAsync([question], EmbeddingPurpose.Query, cancellationToken))[0];
            await store.AddAsync(new AnswerReview
            {
                Question = Clip(question, 2000),
                Answer = Clip(answer, 6000),
                Sources = Clip(sources, 2000),
                Embedding = vector,
                AskedAtUtc = time.GetUtcNow().UtcDateTime,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yazılım cevabı denetime kaydedilemedi");
        }
    }

    /// <summary>Yeni soruya benzeyen, kullanıcının onayladığı düzeltmeler (en fazla 2) — cevap notuna girer.</summary>
    public async Task<string?> NoteForAsync(string question, CancellationToken cancellationToken = default)
    {
        try
        {
            var approved = await store.GetDecidedAsync(ReviewDecision.Onaylandi, cancellationToken);
            if (approved.Count == 0)
                return null;
            var vector = (await embedder.EmbedAsync([question], EmbeddingPurpose.Query, cancellationToken))[0];
            var matches = approved
                .Select(r => (Review: r, Score: r.Embedding.Length == vector.Length ? VectorMath.Cosine(vector, r.Embedding) : 0))
                .Where(m => m.Score >= options.Value.ApplyMinScore)
                .OrderByDescending(m => m.Score).Take(2).Select(m => m.Review).ToList();
            return matches.Count == 0 ? null : Note(matches);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Onaylı düzeltmeler okunamadı");
            return null;
        }
    }

    internal static string Note(IReadOnlyList<AnswerReview> corrections)
    {
        var sb = new StringBuilder("Onaylı düzeltmeler (kullanıcının onayladığı doğru bilgi; kütüphane bölümleriyle çelişirse BUNLAR geçerli). ");
        sb.AppendLine("Soru buna benziyorsa cevabı bu bilgiye göre ver, eski hatayı tekrarlama:");
        foreach (var c in corrections)
            sb.AppendLine($"- Benzer soru: \"{Clip(c.Question, 200)}\" → Doğrusu: {c.Correction}");
        return sb.ToString().TrimEnd();
    }

    // ---- Öğrenme paneli ----

    public Task<IReadOnlyList<AnswerReview>> PendingAsync(CancellationToken cancellationToken = default) =>
        store.GetPendingSuggestionsAsync(cancellationToken);

    /// <summary>Son öğrenilenler (onaylı düzeltmeler, yeniden eskiye): panelde "Öğrendiklerim" ve "Geri al".</summary>
    public async Task<IReadOnlyList<AnswerReview>> LearnedAsync(int max = 30, CancellationToken cancellationToken = default) =>
        (await store.GetDecidedAsync(ReviewDecision.Onaylandi, cancellationToken)).OrderByDescending(r => r.DecidedAtUtc).Take(max).ToList();

    /// <summary>"Geri al": onaylı düzeltme artık kullanılmaz ve reddedilmiş sayılır (benzeri tekrar önerilmez).</summary>
    public async Task<bool> UndoAsync(long id, CancellationToken cancellationToken = default)
    {
        if (await store.GetAsync(id, cancellationToken) is not { Decision: ReviewDecision.Onaylandi } review)
            return false;
        review.Decision = ReviewDecision.Reddedildi;
        review.DecidedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(review, cancellationToken);
        return true;
    }

    /// <summary>Düzeltme resmi bir kaynağa dayanıyor mu: kütüphane klasöründeki bir dosya ya da resmi doküman adresi.</summary>
    public bool HasOfficialReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;
        if (options.Value.ReferenceFolders.Any(f => reference.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return true;
        return UrlPattern().Matches(reference).Any(m => Uri.TryCreate(m.Value, UriKind.Absolute, out var uri)
            && options.Value.OfficialHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"https?://[^\s)\]""',;]+")]
    private static partial System.Text.RegularExpressions.Regex UrlPattern();

    /// <summary>Kurala uyan bekleyen önerileri kendiliğinden onaylar (kural sonradan geldiyse eski öneriler için de).</summary>
    private async Task<int> AutoApprovePendingAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var r in await store.GetPendingSuggestionsAsync(cancellationToken))
        {
            if (!HasOfficialReference(r.Reference))
                continue;
            r.Decision = ReviewDecision.Onaylandi;
            r.AutoApproved = true;
            r.DecidedAtUtc = time.GetUtcNow().UtcDateTime;
            await store.UpdateAsync(r, cancellationToken);
            count++;
        }
        return count;
    }

    public async Task<bool> DecideAsync(long id, bool accept, CancellationToken cancellationToken = default)
    {
        if (await store.GetAsync(id, cancellationToken) is not { IsPendingSuggestion: true } review)
            return false;
        review.Decision = accept ? ReviewDecision.Onaylandi : ReviewDecision.Reddedildi;
        review.DecidedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(review, cancellationToken);
        return true;
    }

    public async Task<int> AcceptAllAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var r in await store.GetPendingSuggestionsAsync(cancellationToken))
            if (await DecideAsync(r.Id, accept: true, cancellationToken))
                count++;
        return count;
    }

    public async Task<int> ApprovedCountAsync(CancellationToken cancellationToken = default) =>
        (await store.GetDecidedAsync(ReviewDecision.Onaylandi, cancellationToken)).Count;

    public async Task<DateTime?> LastAuditUtcAsync(CancellationToken cancellationToken = default) =>
        DateTime.TryParse(await settings.GetInternalAsync(LastAuditKey, cancellationToken), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;

    // ---- Gece denetimi ----

    /// <summary>En son geçen 04:00.</summary>
    public static DateTime LastSlot(DateTime localNow)
    {
        var slot = localNow.Date.Add(DailyAt.ToTimeSpan());
        return slot > localNow ? slot.AddDays(-1) : slot;
    }

    /// <summary>Zamanlayıcı çağırır: son 04:00'ten beri denetim yapılmadıysa ve denetlenecek cevap varsa çalışır.</summary>
    public async Task<string?> TryRunScheduledAsync(CancellationToken cancellationToken = default)
    {
        var localNow = time.GetLocalNow().DateTime;
        if (await LastAuditUtcAsync(cancellationToken) is { } last && TimeZoneInfo.ConvertTimeFromUtc(last, time.LocalTimeZone) >= LastSlot(localNow))
            return null;
        return await AuditAsync(cancellationToken);
    }

    /// <summary>Denetlenmemiş cevapları Claude Code'a okutur, sonuçları kaydeder. Çalışan bir denetim varsa null.</summary>
    public async Task<string?> AuditAsync(CancellationToken cancellationToken = default)
    {
        if (!await _auditGate.WaitAsync(0, cancellationToken))
            return null;
        try
        {
            var earlier = await AutoApprovePendingAsync(cancellationToken);
            var items = await store.GetUnauditedAsync(options.Value.MaxPerAudit, cancellationToken);
            if (items.Count == 0)
            {
                await MarkAuditedAsync(cancellationToken);
                await notifier.NotifyAuditedAsync(0, earlier, (await store.GetPendingSuggestionsAsync(cancellationToken)).Count, cancellationToken);
                return earlier > 0 ? $"Bekleyen {earlier} kaynaklı öneri kendiliğinden onaylandı." : "Denetlenecek yeni yazılım cevabı yok.";
            }

            var status = await agent.GetStatusAsync(cancellationToken);
            if (!status.LoggedIn)
                return "Gece denetimi yapılamadı: " + status.Message; // işaret yazılmaz, sonraki denemede tekrar

            var folder = Path.Combine(options.Value.Folder, time.GetLocalNow().ToString("yyyyMMdd-HHmm"));
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, InputFile), InputJson(items), cancellationToken);

            var references = options.Value.ReferenceFolders.Where(Directory.Exists).ToList();
            var result = await agent.RunAsync(
                new CodeRunRequest(folder, Prompt(items.Count, references))
                {
                    AllowedTools = ["Read", "Glob", "Grep"],
                    ExtraFolders = references,
                    ReadOnly = true,
                },
                step => logger.LogDebug("Denetim: {Step}", step), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "claude-cevabi.txt"), result.Text, cancellationToken);
            if (!result.Ok)
                return "Gece denetimi yarıda kaldı: " + Clip(result.Text, 300);

            var verdicts = ParseVerdicts(result.Text);
            if (verdicts is null)
                return "Gece denetimi bitti ama Claude Code'un cevabı okunamadı (claude-cevabi.txt).";

            var rejected = await store.GetDecidedAsync(ReviewDecision.Reddedildi, cancellationToken);
            int learned = 0, pending = 0;
            foreach (var item in items)
            {
                // Claude bir maddeyi atladıysa "doğru" sayılmaz: denetlenmemiş kalır, sonraki gece tekrar bakılır.
                if (!verdicts.Items.TryGetValue(item.Id, out var v))
                    continue;
                item.AuditedAtUtc = time.GetUtcNow().UtcDateTime;
                item.Verdict = v.Verdict;
                item.Correction = v.Verdict == ReviewVerdict.Dogru ? null : NullIfEmpty(v.Correction);
                item.Reason = NullIfEmpty(v.Reason);
                item.Reference = NullIfEmpty(v.Reference);
                item.PlainSummary = item.Verdict == ReviewVerdict.Dogru ? null : NullIfEmpty(v.Plain);
                if (item.IsPendingSuggestion && rejected.Any(r => r.Embedding.Length == item.Embedding.Length
                        && VectorMath.Cosine(r.Embedding, item.Embedding) >= options.Value.RejectedMinScore))
                    item.Decision = ReviewDecision.Reddedildi; // kullanıcı benzerini reddetmişti
                if (item.IsPendingSuggestion && HasOfficialReference(item.Reference))
                {
                    // tasarım kararı (2026-10-28): resmi kaynağa dayanan düzeltme onay beklemeden geçerli olur.
                    item.Decision = ReviewDecision.Onaylandi;
                    item.AutoApproved = true;
                    item.DecidedAtUtc = time.GetUtcNow().UtcDateTime;
                    learned++;
                }
                else if (item.IsPendingSuggestion)
                    pending++;
                await store.UpdateAsync(item, cancellationToken);
            }

            var reviewed = items.Count(i => i.AuditedAtUtc is not null);
            var report = $"Gece denetimi: {reviewed} cevap okundu, {learned} düzeltme kaynaklı olduğu için öğrenildi, {pending} öneri onay bekliyor."
                + (verdicts.Report is { Length: > 0 } r ? "\n" + r : "");
            await File.WriteAllTextAsync(Path.Combine(folder, "rapor.md"), report, cancellationToken);
            await MarkAuditedAsync(cancellationToken);
            await notifier.NotifyAuditedAsync(reviewed, learned, pending, cancellationToken);
            logger.LogInformation("{Report}", report);
            return report;
        }
        finally
        {
            _auditGate.Release();
        }
    }

    private Task MarkAuditedAsync(CancellationToken cancellationToken) =>
        settings.SetInternalAsync(LastAuditKey, time.GetUtcNow().UtcDateTime.ToString("O"), cancellationToken);

    internal const string InputFile = "girdi.json";

    internal static string InputJson(IReadOnlyList<AnswerReview> items) =>
        new JsonObject
        {
            ["cevaplar"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject
            {
                ["id"] = i.Id,
                ["soru"] = i.Question,
                ["ultron_cevabi"] = i.Answer,
                ["kullandigi_kaynaklar"] = i.Sources,
            }).ToArray()),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    internal static string Prompt(int count, IReadOnlyList<string> references)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sen Ultron'un gece denetmenisin. Ultron, kullanıcının yerel asistanı; küçük bir yerel model (gemma4) ve resmi dokümantasyon kütüphanesiyle yazılım sorularını cevaplıyor.");
        sb.AppendLine($"Bu klasördeki {InputFile} dosyasını oku: Ultron'un bugün verdiği {count} yazılım cevabı var (id, soru, ultron_cevabi, kullandigi_kaynaklar).");
        if (references.Count > 0)
            sb.AppendLine("Gerekirse doğrulamak için şu klasörlerdeki resmi dokümantasyonu Grep/Read ile ara (salt okunur): " + string.Join(", ", references));
        sb.AppendLine("Her cevabı teknik doğruluk ve eksiklik açısından değerlendir:");
        sb.AppendLine("- \"dogru\": doğru ve sorunun özünü karşılıyor (küçük üslup kusurları önemsiz).");
        sb.AppendLine("- \"eksik\": yanlış değil ama sorunun özündeki önemli bir nokta eksik.");
        sb.AppendLine("- \"yanlis\": teknik bir hata, uydurma API/bilgi ya da yanlış yönlendirme var.");
        sb.AppendLine("eksik/yanlis için ayrıca \"sade\" yaz: yazılım bilmeyen birine bu düzeltmenin ne anlama geldiğini anlatan tek, günlük dilde cümle (teknik terim gerekiyorsa kısaca açıkla).");
        sb.AppendLine("eksik/yanlis için \"duzeltme\" yaz: Ultron'un bundan sonra benzer soruda kullanacağı DOĞRU bilgi; Türkçe, 1-4 cümle, gerekirse çok kısa kod; soruyu bilmeyen biri okusa da anlaşılır olsun. \"gerekce\": Ultron'un cevabında neyin yanlış/eksik olduğu, tek cümle. \"kaynak\": dayandığın resmi doküman adresi ya da kütüphane dosyası (biliyorsan).");
        sb.AppendLine("Dosya yazma, komut çalıştırma; sadece oku. Sonunda SADECE şu JSON'u yaz (başka metin yok):");
        sb.AppendLine("{\"degerlendirmeler\":[{\"id\":1,\"karar\":\"dogru|eksik|yanlis\",\"duzeltme\":\"\",\"gerekce\":\"\",\"kaynak\":\"\",\"sade\":\"\"}],\"rapor\":\"Genel gözlem, en fazla 3 cümle (tekrarlayan hata türleri)\"}");
        sb.AppendLine("Her id için tam bir değerlendirme olsun.");
        return sb.ToString();
    }

    internal sealed record ParsedVerdict(ReviewVerdict Verdict, string? Correction, string? Reason, string? Reference, string? Plain = null);

    internal sealed record ParsedAudit(IReadOnlyDictionary<long, ParsedVerdict> Items, string? Report);

    /// <summary>Claude'un son mesajındaki JSON (```json bloğu ya da ilk "{" ile son "}" arası).</summary>
    internal static ParsedAudit? ParseVerdicts(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            var root = JsonNode.Parse(text[start..(end + 1)]);
            if (root?["degerlendirmeler"] is not JsonArray list)
                return null;
            var items = new Dictionary<long, ParsedVerdict>();
            foreach (var node in list)
            {
                if (node?["id"] is not JsonValue idValue || !(idValue.TryGetValue<long>(out var id) || long.TryParse(idValue.ToString(), out id)))
                    continue;
                var verdict = (node["karar"]?.GetValue<string>() ?? "").Trim().ToLowerInvariant() switch
                {
                    "dogru" or "doğru" => ReviewVerdict.Dogru,
                    "eksik" => ReviewVerdict.Eksik,
                    "yanlis" or "yanlış" => ReviewVerdict.Yanlis,
                    _ => (ReviewVerdict?)null,
                };
                if (verdict is { } v)
                    items[id] = new ParsedVerdict(v, node["duzeltme"]?.GetValue<string>(), node["gerekce"]?.GetValue<string>(), node["kaynak"]?.GetValue<string>(), node["sade"]?.GetValue<string>());
            }
            return new ParsedAudit(items, root["rapor"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
