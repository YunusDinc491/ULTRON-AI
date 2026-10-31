using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Coding;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;

namespace Ultron.Core.Pro;

/// <summary>PRO'da Claude'un araştırıp yazdığı, Ultron'un hafızasına giren cevap.</summary>
public sealed class ProAnswer
{
    public long Id { get; init; }

    public required string Question { get; init; }

    public required string Answer { get; set; }

    /// <summary>Claude'un gösterdiği kaynaklar (satır satır).</summary>
    public string Sources { get; set; } = "";

    /// <summary>Soru vektörü: benzer soru gelince bulunur.</summary>
    public float[] Embedding { get; set; } = [];

    /// <summary>"kalici" (kavram, formül, nasıl yapılır) ya da güncel bilginin süresi ("gun", "hafta", "ay").</summary>
    public string Lifetime { get; set; } = "kalici";

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Claude'a gitmeden kaç kez kullanıldı.</summary>
    public int Uses { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Kim öğrendi: "pro" (Claude araştırdı) ya da "medium" (Ultron'un kendi derin web araştırması, Faz 20).</summary>
    public string Origin { get; set; } = "pro";
}

public interface IProAnswerStore
{
    Task<ProAnswer> AddAsync(ProAnswer answer, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProAnswer answer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProAnswer>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>appsettings "Pro" bölümü.</summary>
public sealed class ProOptions
{
    public const string SectionName = "Pro";

    public string WorkFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "pro");

    /// <summary>
    /// Soru–soru benzerliği bundan yüksekse öğrenilen cevap kullanılır (Claude'a gidilmez). Gece denetimi düzeltmeleri 0,70 ile
    /// "ipucu" olarak giriyordu; burada cevabın TAMAMI verildiği için daha sıkı.
    /// </summary>
    public double ReuseMinScore { get; set; } = 0.86;
}

/// <summary>Öğrenilen cevap bulundu.</summary>
public sealed record ProHit(ProAnswer Answer, double Score);

/// <summary>Claude'un araştırması: cevap metni ve kalıcılık; olmadıysa hata.</summary>
public sealed record ProResult(bool Ok, string Answer, string Sources, string Lifetime, string? Error, bool LimitReached = false);

/// <summary>
/// ULTRON PRO (Faz 19, kullanıcının kararları): soruyu Claude Code araştırır (WebSearch/WebFetch, salt okunur; Claude'a SADECE soru
/// gider, hafıza/belge/kişisel bilgi gitmez), cevap Ultron'un hafızasına girer. Benzer soru tekrar gelince — hangi paket seçili
/// olursa olsun — Claude'a gidilmeden bu cevap verilir. Kalıcı bilgi kalıcı; fiyat/kur/haber/sürüm süreli (süresi dolunca yeniden
/// araştırılır). Öğrenme sekmesinde görünür, Geri al var. Benzetme: şefin tarifi deftere yazılır, bir dahakine mutfak kendisi yapar.
/// </summary>
public sealed partial class ProService(
    IProAnswerStore store,
    IProResearcher researcher,
    IEmbeddingModel embedder,
    IOptions<ProOptions> options,
    TimeProvider time,
    ILogger<ProService> logger,
    Chat.IChatModel? chat = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<IReadOnlyList<ProAnswer>> LearnedAsync(CancellationToken cancellationToken = default) => store.GetActiveAsync(cancellationToken);

    public Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default) => store.DeactivateAsync(id, cancellationToken);

    /// <summary>Daha önce PRO'nun öğrendiği, süresi dolmamış, yeterince benzer cevap.</summary>
    public async Task<ProHit?> FindAsync(string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var learned = (await store.GetActiveAsync(cancellationToken)).Where(a => a.Embedding.Length > 0 && (a.ExpiresAtUtc is null || a.ExpiresAtUtc > nowUtc)).ToList();
        if (learned.Count == 0)
            return null;
        var q = (await embedder.EmbedAsync([question], EmbeddingPurpose.Query, cancellationToken))[0];
        var best = learned.Where(a => a.Embedding.Length == q.Length)
            .Select(a => new ProHit(a, VectorMath.Cosine(q, a.Embedding)))
            .MaxBy(h => h.Score);
        if (best is null || best.Score < options.Value.ReuseMinScore)
            return null;
        best.Answer.Uses++;
        await store.UpdateAsync(best.Answer, cancellationToken);
        logger.LogInformation("PRO hafızasından cevap ({Score:0.00}): {Question}", best.Score, question);
        return best;
    }

    /// <summary>Bağlı hesap (Claude, Codex ya da Gemini) soruyu internette araştırır; başarılıysa cevap hafızaya yazılır.</summary>
    /// <param name="progress">Araştırmacının adımları ("Arıyor: …", "Okuyor: …").</param>
    public async Task<ProResult> ResearchAsync(string question, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var status = await researcher.GetStatusAsync(cancellationToken);
        if (!status.Ready)
            return new ProResult(false, "", "", "", status.Message);
        await _gate.WaitAsync(cancellationToken); // aynı anda tek PRO araştırması
        try
        {
            var folder = Path.Combine(options.Value.WorkFolder, time.GetLocalNow().ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            progress($"{status.Provider} araştırıyor");
            var result = await researcher.RunAsync(folder, Prompt(question, time.GetLocalNow().DateTime), step => progress(step), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "cevap.md"), $"# {question}\n\n{result.Text}", cancellationToken);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.Text))
                return new ProResult(false, "", "", "",
                    result.Error ?? (result.LimitReached ? $"{status.Provider} kullanım limiti doldu." : $"{status.Provider} cevap veremedi."), result.LimitReached);

            var (answer, sources, lifetime) = Parse(result.Text);
            await SaveAsync(question, answer, sources, lifetime, cancellationToken);
            return new ProResult(true, answer, sources, lifetime, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cevap "bulamadım / kontrol etmelisin" diyor mu? Böyle cevaplar ne hafızaya ne önbelleğe girer.</summary>
    public static bool LooksUnsure(string answer)
    {
        var normalized = Text.TurkishText.Normalize(answer);
        return Unsure.Any(u => normalized.Contains(u, StringComparison.Ordinal));
    }

    private static readonly string[] Unsure =
    [
        "bulamadim", "bulunamadi", "bilgi yok", "net bir bilgi", "net bir tarih", "kesin bilgi", "emin degilim", "ulasamadim", "kaynaklarda yer almiyor",
        "kaynaklarda bulunmuyor", "bulunmamaktadir", "yer almamaktadir", "kontrol etmeniz gerek", "kontrol etmelisin", "resmi duyurularini kontrol", "veremem", "saglayamiyorum",
    ];

    /// <summary>
    /// Ultron Medium'un kendi bulduğu iyi cevabı hafızaya alır (Faz 20, kullanıcı: "bana sormadan da internette güzel sonuçlar bulup
    /// hafızaya alabilsin"). Sadece en az 2 kaynaklı, yeterince uzun ve "bulamadım" demeyen cevaplar; kalıcılığı model söyler.
    /// </summary>
    /// <returns>Kaydedildiyse kalıcılık ("kalici", "gun"…), yoksa null.</returns>
    public async Task<string?> LearnFromWebAsync(string question, string answer, IReadOnlyList<Web.WebSource> sources, CancellationToken cancellationToken = default)
    {

        if (sources.Count < 2 || answer.Length < 250 || LooksUnsure(answer))
            return null;
        var lifetime = await LifetimeAsync(question, answer, cancellationToken);
        var list = string.Join("\n", sources.Select(s => $"[{s.Number}] {s.Title} — {s.Url}"));
        await SaveAsync(question, answer, list, lifetime, cancellationToken, origin: "medium");
        logger.LogInformation("Medium cevabı hafızaya alındı ({Lifetime}): {Question}", lifetime, question);
        return lifetime;
    }

    private const string LifetimeSchema = """{"type":"object","properties":{"kalicilik":{"type":"string","enum":["kalici","gun","hafta","ay"]}},"required":["kalicilik"]}""";

    private async Task<string> LifetimeAsync(string question, string answer, CancellationToken cancellationToken)
    {
        if (chat is null)
            return "hafta";
        try
        {
            var json = await chat.CompleteJsonAsync([
                new Chat.ChatMessage(Chat.Role.System, "Bu soru-cevaptaki bilgi ne kadar süre doğru kalır? kalici: kavram, formül, tarihî olay, nasıl yapılır; gun: fiyat, kur, hava, haber, maç; hafta: güncel durum, duyuru, kısa süreli liste; ay: sürüm, dönemlik takvim, yıllık liste."),
                new Chat.ChatMessage(Chat.Role.User, $"Soru: {question}\n\nCevabın başı: {(answer.Length > 600 ? answer[..600] : answer)}"),
            ], LifetimeSchema, cancellationToken);
            var lifetime = System.Text.Json.Nodes.JsonNode.Parse(json)?["kalicilik"]?.GetValue<string>();
            return lifetime is "kalici" or "gun" or "hafta" or "ay" ? lifetime : "hafta";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Kalıcılık belirlenemedi");
            return "hafta";
        }
    }

    private async Task SaveAsync(string question, string answer, string sources, string lifetime, CancellationToken cancellationToken, string origin = "pro")
    {
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var vector = (await embedder.EmbedAsync([question], EmbeddingPurpose.Query, cancellationToken))[0];
        // Aynı soru tekrar araştırıldıysa (süresi dolmuştu) eskisi kapanır.
        foreach (var old in await store.GetActiveAsync(cancellationToken))
            if (old.Embedding.Length == vector.Length && VectorMath.Cosine(vector, old.Embedding) >= options.Value.ReuseMinScore)
                await store.DeactivateAsync(old.Id, cancellationToken);
        await store.AddAsync(new ProAnswer
        {
            Question = question.Length > 500 ? question[..500] : question, Answer = answer, Sources = sources, Embedding = vector, Lifetime = lifetime,
            ExpiresAtUtc = lifetime switch { "gun" => nowUtc.AddDays(1), "hafta" => nowUtc.AddDays(7), "ay" => nowUtc.AddDays(30), _ => null },
            CreatedAtUtc = nowUtc,
            Origin = origin,
        }, cancellationToken);
    }

    internal static string Prompt(string question, DateTime now) => $"""
        Ultron (kullanıcının yerel kişisel asistanı) Ultron Pro modunda bu soruyu sana soruyor. Bugün {now:dd.MM.yyyy}.
        Soruyu WebSearch ve WebFetch ile araştır (Türkçe ve İngilizce kaynaklar; güvenilir ve güncel olanlara öncelik ver, gerekirse birkaç kaynağı karşılaştır).
        Cevabı TÜRKÇE yaz, Claude üslubuyla: ilk cümle doğrudan cevap; sonra önemli ayrıntılar ve nedenleri; gerekiyorsa kısa başlıklar, maddeler ya da tablo; sonda tek cümlelik öneri/özet. Kaynakları cümlelerde [1] gibi numarayla göster.
        Dosya yazma, komut çalıştırma. Çıktın SADECE şu biçimde olsun:
        <cevap>
        (cevap metni)
        </cevap>
        <kaynaklar>
        [1] Başlık — https://…
        </kaynaklar>
        <kalicilik>kalici | gun | hafta | ay</kalicilik>
        kalicilik: bilgi zamanla değişmiyorsa (kavram, formül, tarih, nasıl yapılır) "kalici"; fiyat, kur, hava, haber için "gun"; sürüm, liste, güncel durum için "hafta" ya da "ay".

        Soru: {question}
        """;

    [GeneratedRegex(@"<(cevap|kaynaklar|kalicilik)>\s*(.*?)\s*</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Section();

    /// <summary>Claude'un çıktısı → cevap, kaynaklar, kalıcılık. Etiket yoksa metnin tamamı cevap, kalıcılık "hafta" (temkinli).</summary>
    internal static (string Answer, string Sources, string Lifetime) Parse(string text)
    {
        var parts = Section().Matches(text).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => m.Groups[2].Value.Trim());
        var answer = parts.GetValueOrDefault("cevap") is { Length: > 0 } a ? a : text.Trim();
        var sources = parts.GetValueOrDefault("kaynaklar") ?? "";
        var lifetime = parts.GetValueOrDefault("kalicilik")?.ToLowerInvariant() switch
        {
            "kalici" or "kalıcı" => "kalici",
            "gun" or "gün" => "gun",
            "ay" => "ay",
            _ => "hafta",
        };
        return (answer, sources, lifetime);
    }

    /// <summary>Kullanıcıya gösterilecek metin: cevap + kaynaklar.</summary>
    public static string Display(string answer, string sources) =>
        sources.Length == 0 ? answer : new StringBuilder(answer).AppendLine().AppendLine().AppendLine("**Kaynaklar**").Append(sources).ToString();
}
