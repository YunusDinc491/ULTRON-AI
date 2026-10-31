using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Chat;
using Ultron.Core.Settings;

namespace Ultron.Core.Reminders;

/// <summary>
/// Hatırlatmayı kullanıcıya söylenecek samimi tek cümleye çevirir. "Hatırlatma: Su iç" gece yarısı robot sesiyle
/// korkutucu ve emir gibiydi; kullanıcı: "hatırlatmamı istemiştin, su içmen gerekiyor gibi bir cümle kursun".
/// Hitap, kullanıcının seçtiği ayardan gelir (Faz 26; boşsa isimsiz). Model cevap veremezse aynı kalıpta hazır cümle kullanılır.
/// </summary>
public sealed class ReminderPhrasing(IChatModel chat, ILogger<ReminderPhrasing> logger, UltronSettings? settings = null)
{
    internal static string Prompt(string? address)
    {
        string Say(string sentence) => UltronSettings.Addressed(address, sentence);
        var how = string.IsNullOrWhiteSpace(address) ? "İsimle hitap etme" : $"\"{address.Trim()}\" diye hitap et";
        return $"""
            Kullanıcının kurduğu bir hatırlatmanın zamanı geldi. Ona söyleyeceğin TEK, samimi, doğal Türkçe cümleyi yaz.
            {how} ve "hatırlatmamı istemiştin" de; yapılacak işi ikinci tekil şahısla, nazikçe söyle.
            Komut verme, korkutma, emoji ve tırnak kullanma.
            Örnekler:
            "Su iç" → {Say("hatırlatmamı istemiştin, su içmen gerekiyor.")}
            "Annemi ara" → {Say("hatırlatmamı istemiştin, anneni araman gerekiyor.")}
            "İlacımı iç" → {Say("hatırlatmamı istemiştin, ilacını içme vakti geldi.")}
            Zamanı kaçırılmış hatırlatmada şöyle başla: {Say("sen yokken bir hatırlatman vardı, …")}
            """;
    }

    private const string Schema = """{"type":"object","properties":{"cumle":{"type":"string"}},"required":["cumle"]}""";

    public async Task<string> PhraseAsync(string text, bool missed, CancellationToken cancellationToken = default)
    {
        var address = settings is null ? null : await settings.GetAsync(UltronSettings.UserAddress, cancellationToken);
        try
        {
            var json = await chat.CompleteJsonAsync(
                [new ChatMessage(Role.System, Prompt(address)), new ChatMessage(Role.User, $"Hatırlatma: \"{text}\"{(missed ? " (zamanı kaçırıldı)" : "")}")],
                Schema, cancellationToken);
            var sentence = JsonDocument.Parse(json).RootElement.GetProperty("cumle").GetString()?.Trim().Trim('"');
            if (sentence is { Length: >= 10 and <= 220 })
                return sentence;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Hatırlatma cümlesi üretilemedi, hazır cümle kullanılıyor");
        }
        return Fallback(text, missed, address);
    }

    public static string Fallback(string text, bool missed, string? address = null) =>
        UltronSettings.Addressed(address, missed ? $"sen yokken bir hatırlatman vardı: {text}." : $"hatırlatmamı istemiştin: {text}.");
}
