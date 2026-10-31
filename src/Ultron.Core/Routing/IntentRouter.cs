using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Chat;

namespace Ultron.Core.Routing;

/// <summary>
/// Her mesajı tek bir <see cref="MessageIntent"/>'e ayırır. Trafik polisi gibi: kendisi cevap vermez,
/// mesajı doğru yola (hafıza, web, araç…) yönlendirir. Prompt, Faz 1 tur 2'de 7/7 alan sürümün genişletilmişi.
/// </summary>
public sealed class IntentRouter(IChatModel chat, ILogger<IntentRouter> logger)
{
    private static readonly string IntentJsonArray =
        "[" + string.Join(", ", Enum.GetNames<MessageIntent>().Select(n => $"\"{n}\"")) + "]";

    internal const string Prompt =
        """
        Görevin: kullanıcının mesajını tek bir sınıfa ayırmak.
        Sınıflar:
        - Kisisel = kullanıcının kendi hayatı, ailesi, evcil hayvanı, dersleri, geçmişte söyledikleri hakkında soru ('neydi / nedir / hangisiydi').
        - GenelBilgi = zamanla değişmeyen genel bilgi: tanım, açıklama, nasıl yapılır, tarihî olay, bilimsel kavram (yazılım dışı).
        - Yazilim = programlama ve yazılım geliştirme sorusu: kod, dil (C#, JavaScript, TypeScript, Kotlin, Dart, Swift), framework (React, Next.js, ASP.NET Core, EF Core, Flutter, React Native, Node.js), API, CSS/HTML, Git, hata mesajı, mimari, "bu kod neden çalışmıyor".
        - Guncel = zamanla değişen bilgi: döviz kuru, fiyat, hava durumu, haber, maç sonucu, bugün/şu an olan şeyler.
        - Belge = SADECE kullanıcı açıkça yüklediği bir dosya, not, PDF veya belgeden bahsederse.
        - Komut = bilgisayarda veya dünyada bir İŞLEM yapılmasını isteme: hatırlatma kurma, alarm, uygulama açma, dosya işlemi, mesaj gönderme. Metin yazma, açıklama, özetleme, liste hazırlama, kendini tanıtma, çeviri gibi CEVAP İSTEKLERİ Komut DEĞİLDİR (bunlar Sohbet veya GenelBilgi'dir).
        - Sohbet = selamlaşma, hal hatır, teşekkür, kullanıcının kendisi hakkında bilgi vermesi (soru sormadan).
        Önceki asistan mesajı verilmişse kısa cevapları ('evet', 'olur') onun bağlamında değerlendir.
        SADECE JSON döndür.
        """;

    internal static readonly string Schema =
        $$"""
        { "type": "object", "properties": { "sinif": { "type": "string", "enum": {{IntentJsonArray}} } }, "required": ["sinif"] }
        """;

    /// <summary>Model hata verirse veya anlaşılmaz cevap dönerse <see cref="MessageIntent.Sohbet"/>: en zararsız yol (web yok, araç yok).</summary>
    public async Task<MessageIntent> ClassifyAsync(string message, string? previousAssistantMessage = null, CancellationToken cancellationToken = default)
    {
        var user = previousAssistantMessage is null
            ? message
            : $"Önceki asistan mesajı: {previousAssistantMessage}\nKullanıcının mesajı: {message}";

        try
        {
            var json = await chat.CompleteJsonAsync([new(Role.System, Prompt), new(Role.User, user)], Schema, cancellationToken);
            var intent = Parse(json);

            // Model "liste yaz", "kendini tanıt" gibi cevap isteklerini de Komut sanabiliyor (Faz 5 canlı denemesi).
            // İşlem fiili yoksa Sohbet'e düşür: yanlış Komut, normal bir isteği "yapamıyorum" diye geri çevirir.
            if (intent == MessageIntent.Komut && !ActionWords.Contains(message))
            {
                logger.LogInformation("Komut sanıldı ama işlem fiili yok, Sohbet sayıldı: {Message}", message);
                return MessageIntent.Sohbet;
            }

            return intent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Mesaj sınıflandırılamadı, Sohbet varsayıldı: {Message}", message);
            return MessageIntent.Sohbet;
        }
    }

    internal static MessageIntent Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.TryGetProperty("sinif", out var s) ? s.GetString() : null;
        return Enum.TryParse<MessageIntent>(value, ignoreCase: true, out var intent) ? intent : MessageIntent.Sohbet;
    }
}
