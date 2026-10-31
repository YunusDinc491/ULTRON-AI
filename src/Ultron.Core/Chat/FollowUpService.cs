using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Memories;

namespace Ultron.Core.Chat;

/// <param name="Label">Düğmede görünen kısa etiket (1-4 kelime): "1000-3000 TL".</param>
/// <param name="Answer">Seçilince gönderilen, tek başına anlaşılır tam cümle: "Bütçem 1000-3000 TL arası."
/// Bilgi çıkarma bu cümleden hafızaya yazar.</param>
public sealed record FollowUpOption(string Label, string Answer);

/// <summary>Kullanıcıya tek tıkla cevaplayabileceği kısa bir soru.</summary>
public sealed record FollowUpQuestion(string Question, IReadOnlyList<FollowUpOption> Options);

/// <summary>
/// Cevaptan sonra, cevabı kişiselleştirecek eksik bilgiler için zahmetsiz (seçenekli) sorular üretir.
/// Kullanıcının seçtiği cevaplar normal mesaj olarak gelir ve bilgi çıkarma onları hafızaya yazar:
/// Ultron her soruda biraz daha fazla öğrenir, aynı şeyi ikinci kez sormaz.
/// </summary>
public sealed class FollowUpService(IChatModel chat, ILogger<FollowUpService> logger)
{
    public const int MaxQuestions = 3;
    public const int MaxOptions = 4;
    private const int MaxLabelLength = 32;

    internal const string Prompt =
        """
        Görevin: asistan kullanıcının mesajına cevap verdi. Bu cevabı kullanıcıya göre DAHA İYİ kişiselleştirmek için kullanıcıya sorulabilecek en fazla 3 kısa takip sorusu üret.
        Kurallar:
        - Sadece cevabı gerçekten iyileştirecek ve "Hafızada bilinenler" içinde OLMAYAN bilgileri sor. Sorular kısa olsun (en fazla 8 kelime).
        - Her soruya 2-4 seçenek yaz. Her seçeneğin iki alanı var:
          etiket: düğmede görünecek ÇOK kısa ifade, 1-4 kelime (ör. "1000 TL'ye kadar", "Kitap", "Doğa").
          cumle: kullanıcının ağzından, tek başına anlaşılır tam cümle (ör. "Bütçem 1000 TL'ye kadar.", "Kız kardeşim kitap okumayı seviyor.").
        - Selamlaşma, teşekkür, genel bilgi sorusu (tanım, açıklama), tamamlanmış bir iş veya bilgi zaten yeterliyse BOŞ liste döndür.
        SADECE JSON döndür.
        """;

    internal const string Schema =
        """
        { "type": "object",
          "properties": { "sorular": { "type": "array", "items": { "type": "object",
              "properties": { "soru": { "type": "string" },
                              "secenekler": { "type": "array", "items": { "type": "object",
                                  "properties": { "etiket": { "type": "string" }, "cumle": { "type": "string" } },
                                  "required": ["etiket", "cumle"] } } },
              "required": ["soru", "secenekler"] } } },
          "required": ["sorular"] }
        """;

    /// <summary>Hata olursa boş liste: takip soruları süs, asıl cevabı asla engellememeli.</summary>
    public async Task<IReadOnlyList<FollowUpQuestion>> SuggestAsync(
        string userText, string answer, MemoryRecall recall, CancellationToken cancellationToken = default)
    {
        var context = new StringBuilder();
        context.AppendLine($"Kullanıcının mesajı: {userText}");
        context.AppendLine($"Asistanın cevabı: {answer}");
        context.AppendLine("Hafızada bilinenler:");
        foreach (var m in recall.Core.Concat(recall.Relevant))
            context.AppendLine($"- {m.Content}");

        try
        {
            var json = await chat.CompleteJsonAsync(
                [new(Role.System, Prompt), new(Role.User, context.ToString())], Schema, cancellationToken);
            return Parse(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Takip soruları üretilemedi");
            return [];
        }
    }

    internal static IReadOnlyList<FollowUpQuestion> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sorular", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<FollowUpQuestion>();
        foreach (var item in items.EnumerateArray())
        {
            var question = item.TryGetProperty("soru", out var q) ? q.GetString()?.Trim() : null;
            var options = item.TryGetProperty("secenekler", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().Select(ParseOption).OfType<FollowUpOption>().DistinctBy(x => x.Label).Take(MaxOptions).ToList()
                : [];

            // Seçeneksiz ya da tek seçenekli soru "zahmetsiz" değil: atla.
            if (!string.IsNullOrWhiteSpace(question) && options.Count >= 2)
                result.Add(new(question, options));
            if (result.Count == MaxQuestions)
                break;
        }

        return result;
    }

    /// <summary>Etiket eksikse ya da çok uzunsa cümleden kısaltılır; cümle eksikse etiket kullanılır.</summary>
    private static FollowUpOption? ParseOption(JsonElement e)
    {
        string? label = null, answer = null;
        if (e.ValueKind == JsonValueKind.String)
            answer = e.GetString();
        else if (e.ValueKind == JsonValueKind.Object)
        {
            label = e.TryGetProperty("etiket", out var l) ? l.GetString()?.Trim() : null;
            answer = e.TryGetProperty("cumle", out var c) ? c.GetString()?.Trim() : null;
        }

        answer = string.IsNullOrWhiteSpace(answer) ? label : answer;
        label = string.IsNullOrWhiteSpace(label) ? answer : label;
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(answer))
            return null;

        if (label.Length > MaxLabelLength)
            label = label[..(label.LastIndexOf(' ', MaxLabelLength - 1) is > 10 and var cut ? cut : MaxLabelLength - 1)].TrimEnd(',', '.', ' ') + "…";

        return new FollowUpOption(label, answer);
    }
}
