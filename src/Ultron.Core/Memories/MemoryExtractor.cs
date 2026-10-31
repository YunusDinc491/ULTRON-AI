using System.Text.Json;
using Ultron.Core.Chat;

namespace Ultron.Core.Memories;

/// <param name="Category">Mevcut kategori anahtarı; <paramref name="New"/> doluysa klasör açılana kadar boş.</param>
/// <param name="New">Uygun klasör yok: bu çekmecede açılacak (ya da benzeri bulunacak) yeni klasör.</param>
public sealed record ExtractedFact(string Category, string Content, string Drawer = MemoryDrawers.Patron, ProposedCategory? New = null);

/// <summary>Modelin önerdiği yeni kategori (tasarım kararı, 2026-10-27: Ultron kendisi açar ve haber verir).</summary>
public sealed record ProposedCategory(string Name, string Description);

public sealed record ExtractionResult(IReadOnlyList<ExtractedFact> Facts)
{
    public static readonly ExtractionResult Empty = new([]);
}

/// <summary>Bir kullanıcı mesajından kalıcı bilgileri çıkarır; kategori listesi veritabanından gelir.</summary>
public sealed partial class MemoryExtractor(IChatModel chat, ICategoryStore categories)
{
    /// <param name="previousAssistant">Ultron'un bir önceki mesajı: "Evet" gibi kısa cevaplar ancak onunla anlaşılır.</param>
    public async Task<ExtractionResult> ExtractAsync(string userText, CancellationToken cancellationToken = default, string? previousAssistant = null)
    {
        var all = await categories.GetAllAsync(cancellationToken);
        var json = await chat.CompleteJsonAsync(
            [new(Role.System, MemoryPrompts.Extraction(DateTime.Now, all)), new(Role.User, MemoryPrompts.WithContext(userText, previousAssistant))],
            MemoryPrompts.ExtractionSchema(all),
            cancellationToken);

        return new(KeepFacts(userText, Parse(json, all).Facts));
    }

    // Gece sınavı (2026-10-28): 40 soruda hafızaya 13 sahte kayıt girdi — soru "bilgi" sanıldı ("… hakkında bilgi edinmek istiyor",
    // "… sorusu soruldu", "… yazılması talep edildi") ve sorulan konu BİLGİ'ye "öğrenildi" diye yazıldı. Prompt kuralı tutmadığı için kodla.
    private static readonly string[] MetaPhrases =
    [
        "sorusu", "sorular", "soru sor", "sordu", "soruldu", "soruyor", "bilgi edinmek", "bilgi almak", "ogrenmek istiyor", "merak ediyor", "talep", "istendi",
        "istedi", "rica", "yardim istiyor", "hakkinda bilgi", "nasil yapilacagini", "aciklanmasini", "anlatilmasini", "yazilmasi", "yapilmasi",
    ];

    /// <summary>Kullanıcı açıkça öğretiyorsa ("şunu bil", "unutma", "aklında tut") genel bilgi BİLGİ çekmecesine girer; yoksa girmez.</summary>
    private static readonly string[] TeachPhrases = ["bil ", "bilesin", "unutma", "aklinda tut", "aklinda bulunsun", "not et", "kaydet", "ogren ", "ogrenmeni", "hatirla ki", "bilgin olsun"];

    /// <summary>Soru/istek anlatan kayıtlar ve kullanıcı öğretmediği hâlde BİLGİ'ye giden kayıtlar atılır.</summary>
    internal static IReadOnlyList<ExtractedFact> KeepFacts(string userText, IReadOnlyList<ExtractedFact> facts)
    {
        var message = Text.TurkishText.Normalize(userText) + " ";
        var teaching = TeachPhrases.Any(p => message.Contains(p, StringComparison.Ordinal));
        return facts.Where(f =>
        {
            var content = Text.TurkishText.Normalize(f.Content);
            return !MetaPhrases.Any(p => content.Contains(p, StringComparison.Ordinal)) && (f.Drawer != MemoryDrawers.Bilgi || teaching);
        }).ToList();
    }

    /// <summary>Yeni klasör, mevcut <paramref name="existing"/> ile aynı konu mu (anlamca yakın ama emin olunamayan durum).</summary>
    public async Task<bool> SameTopicAsync(string drawer, ProposedCategory proposed, MemoryCategory existing, CancellationToken cancellationToken = default)
    {
        var json = await chat.CompleteJsonAsync(
            [new(Role.System, MemoryPrompts.CategoryMatch(MemoryDrawers.Find(drawer)?.Name ?? drawer, proposed.Name, proposed.Description, existing)), new(Role.User, "Karar ver.")],
            MemoryPrompts.CategoryMatchSchema,
            cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("ayni", out var same) && same.ValueKind == JsonValueKind.True;
    }

    /// <summary>Web/araştırma cevabından öğrenilen kalıcı bilgiler (BİLGİ çekmecesi).</summary>
    public async Task<IReadOnlyList<ExtractedFact>> ExtractLearnedAsync(string question, string answer, CancellationToken cancellationToken = default)
    {
        var all = await categories.GetAllAsync(cancellationToken);
        var json = await chat.CompleteJsonAsync(
            [new(Role.System, MemoryPrompts.Learned(all)), new(Role.User, $"SORU: {question}\n\nULTRON'UN CEVABI: {answer}")],
            MemoryPrompts.LearnedSchema(all),
            cancellationToken);
        return ParseLearned(json, all);
    }

    internal static IReadOnlyList<ExtractedFact> ParseLearned(string json, IReadOnlyList<MemoryCategory> categories)
    {
        var inBilgi = categories.Where(c => c.Drawer == MemoryDrawers.Bilgi).ToList();
        return ParseItems(json, inBilgi, _ => MemoryDrawers.Bilgi)
            .Select(f => f with { Content = Citation().Replace(f.Content, "").Trim() })
            .Where(f => f.Content.Length is >= 8 and <= 400)
            .Take(3)
            .ToList();
    }

    /// <summary>Şemaya rağmen bozuk gelen maddeler (bilinmeyen kategori, boş metin) sessizce atlanır.</summary>
    internal static ExtractionResult Parse(string json, IReadOnlyList<MemoryCategory> categories) =>
        new(ParseItems(json, categories, item => item.TryGetProperty("cekmece", out var d) ? d.GetString() : null));

    /// <summary>
    /// Mevcut klasör seçildiyse onun çekmecesi geçerlidir (model çekmeceyi yanlış yazsa bile). YENI ise çekmece modelden gelir,
    /// geçersizse PATRON; yeni klasör adı yoksa ya da uygunsuzsa madde atlanır.
    /// </summary>
    private static List<ExtractedFact> ParseItems(string json, IReadOnlyList<MemoryCategory> categories, Func<JsonElement, string?> drawerOf)
    {
        using var doc = JsonDocument.Parse(json);
        var facts = new List<ExtractedFact>();
        if (!doc.RootElement.TryGetProperty("bilgiler", out var items) || items.ValueKind != JsonValueKind.Array)
            return facts;

        var byKey = categories.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var content = item.TryGetProperty("bilgi", out var b) ? b.GetString()?.Trim() : null;
            var category = item.TryGetProperty("kategori", out var c) ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(content) || category is null)
                continue;

            if (byKey.TryGetValue(category, out var existing))
            {
                facts.Add(new(existing.Key, content, existing.Drawer));
                continue;
            }

            var name = item.TryGetProperty("yeni_ad", out var n) ? n.GetString()?.Trim() : null;
            if (name is not { Length: >= 2 and <= 40 })
                continue;
            var description = item.TryGetProperty("yeni_aciklama", out var d) ? d.GetString()?.Trim() ?? "" : "";
            var drawer = MemoryDrawers.Find(drawerOf(item))?.Key ?? MemoryDrawers.Patron;
            facts.Add(new("", content, drawer, new ProposedCategory(name, description)));
        }
        return facts;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\[\d+\]")]
    private static partial System.Text.RegularExpressions.Regex Citation();
}
