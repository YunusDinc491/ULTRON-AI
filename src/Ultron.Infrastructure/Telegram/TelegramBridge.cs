using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;
using Ultron.Core.Voice;
using Ultron.Infrastructure.Voice;

namespace Ultron.Infrastructure.Telegram;

/// <summary>Telegram kanalının anlık durumu (masaüstündeki /durum ve bildirim gönderimi için).</summary>
public sealed class TelegramState
{
    public bool Configured { get; internal set; }

    public string? BotUsername { get; internal set; }

    /// <summary>Eşleşmiş sohbet (kullanıcının Telegram hesabı); yoksa null.</summary>
    public long? ChatId { get; internal set; }

    /// <summary>Henüz eşleşme yokken masaüstünde gösterilen tek kullanımlık kod.</summary>
    public string PairingCode { get; } = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);

    public string? LastError { get; internal set; }

    /// <summary>Claude Code'un son sorusunun Telegram mesajı: bu mesaja verilen yanıt (reply) cevap olarak iletilir.</summary>
    public long? CodingQuestionMessageId { get; set; }

    public string Describe() =>
        !Configured ? "KAPALI — bot token'ı kaydedilmedi"
        : LastError is not null && BotUsername is null ? "HATA — " + LastError
        : ChatId is null ? $"EŞLEŞME BEKLİYOR — Telegram'da @{BotUsername} botuna: /baglan {PairingCode}"
        : $"BAĞLI — @{BotUsername}";
}

/// <summary>
/// Telegram botu: uzun yoklamayla (bilgisayarı internete açmadan) mesajları alır. Sadece eşleşmiş sohbete hizmet eder.
/// Yazı ve sesli mesaj masaüstüyle AYNI konuşmaya gider ("hangi kapıdan girersen gir aynı Ultron"); sesle sorana sesle de cevap.
/// [✅ Okudum] düğmesi tekrar eden hatırlatmayı susturur.
/// </summary>
internal sealed partial class TelegramBridge(
    TelegramApi api,
    TelegramState state,
    IOptions<TelegramOptions> options,
    ChatOrchestrator orchestrator,
    IConversationStore conversations,
    ReminderService reminders,
    UltronSettings settings,
    ISpeechToText speechToText,
    ITextToSpeech textToSpeech,
    DocumentService documents,
    ApprovalBroker approvals,
    Ultron.Core.Coding.CodingService coding,
    ILogger<TelegramBridge> logger) : BackgroundService
{
    /// <summary>Sesli mesaja sesli cevap (kapalı: tasarım kararı, 2026-10-30 — her zaman yazıyla).</summary>
    internal const bool VoiceRepliesOnTelegram = false;

    /// <summary>Telegram botları en fazla 20 MB'lık dosya indirebilir.</summary>
    internal const long MaxDownloadBytes = 20L * 1024 * 1024;

    public const string ChatIdKey = "telegram_chat_id";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        state.Configured = options.Value.Enabled;
        if (!state.Configured)
        {
            logger.LogInformation("Telegram kapalı: bot token'ı kaydedilmemiş (dotnet user-secrets set \"Telegram:BotToken\" …)");
            return;
        }
        if (long.TryParse(await settings.GetInternalAsync(ChatIdKey, stoppingToken), out var paired))
            state.ChatId = paired;

        long offset = 0;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                state.BotUsername ??= (await api.GetMeAsync(stoppingToken)).Username;
                foreach (var update in await api.GetUpdatesAsync(offset, stoppingToken))
                {
                    offset = update.UpdateId + 1;
                    await HandleAsync(update, stoppingToken);
                }
                state.LastError = null;
                failures = 0;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // İnternet yoksa ya da token yanlışsa: günlüğe yaz, artan aralıkla tekrar dene (en fazla 1 dk).
                state.LastError = ex.Message;
                logger.LogWarning("Telegram: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * ++failures)), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TgUpdate update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback, cancellationToken);
            return;
        }
        if (update.Message is not { } message)
            return;

        var chatId = message.Chat.Id;
        var text = message.Text?.Trim() ?? "";
        if (state.ChatId != chatId)
        {
            await HandleStrangerAsync(chatId, text, cancellationToken);
            return;
        }

        try
        {
            await HandleOwnerAsync(message, text, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Telegram mesajı işlenemedi");
            await api.SendMessageAsync(chatId, "Bir sorun oldu, bir kez daha dener misin?", cancellationToken: cancellationToken);
        }
    }

    /// <summary>Eşleşmemiş sohbet: sadece doğru kodla /baglan kabul edilir.</summary>
    private async Task HandleStrangerAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var match = PairCommand().Match(text);
        if (state.ChatId is null && match.Success && match.Groups[1].Value == state.PairingCode)
        {
            state.ChatId = chatId;
            await settings.SetInternalAsync(ChatIdKey, chatId.ToString(CultureInfo.InvariantCulture), cancellationToken);
            logger.LogInformation("Telegram eşleşti: {ChatId}", chatId);
            await api.SendMessageAsync(chatId,
                "Bağlandık. Artık buradan yazabilir ya da sesli mesaj atabilirsin; bilgisayar başında değilken hatırlatmalar da buraya gelecek. Yeni konuşma için /yeni.",
                cancellationToken: cancellationToken);
            return;
        }
        logger.LogInformation("Telegram: eşleşmemiş sohbetten mesaj yok sayıldı ({ChatId})", chatId);
        if (state.ChatId is null)
            await api.SendMessageAsync(chatId, "Bu bot özel. Masaüstünde /durum'daki kodla eşleştir: /baglan KOD", cancellationToken: cancellationToken);
    }

    private async Task HandleOwnerAsync(TgMessage message, string text, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        if (text.StartsWith("/start", StringComparison.Ordinal))
        {
            await api.SendMessageAsync(chatId, "Buradayım. Yaz ya da sesli mesaj at.", cancellationToken: cancellationToken);
            return;
        }
        if (text.StartsWith("/yeni", StringComparison.Ordinal))
        {
            await conversations.StartAsync(cancellationToken);
            await api.SendMessageAsync(chatId, "Yeni konuşma başladı.", cancellationToken: cancellationToken);
            return;
        }

        // Fotoğraf (ya da dosya olarak gönderilmiş görsel): açıklaması soru olur, boşsa "ne var, yazıyı oku" (Faz 11).
        ImageInput? image = null;
        var photoId = message.Photo is { Length: > 0 } photos ? photos.MaxBy(p => p.Width * p.Height)!.FileId
            : message.Document is { FileName: { } docName } d && ImageNames().IsMatch(docName) ? d.FileId : null;
        if (photoId is not null)
        {
            await api.SendTypingAsync(chatId, cancellationToken);
            image = new ImageInput(await api.DownloadFileAsync(photoId, cancellationToken));
            text = message.Caption?.Trim() ?? "";
        }

        // Dosya: bilgi tabanına eklenir; açıklama yazıldıysa ("bunda entropi ne diyor?") ardından soru olarak cevaplanır.
        else if (message.Document is { } file)
        {
            if (!await AddDocumentAsync(chatId, file, cancellationToken))
                return;
            text = message.Caption?.Trim() ?? "";
            if (text.Length == 0)
                return;
        }

        var spoken = false;
        if (message.Voice is { } voice)
        {
            await api.SendTypingAsync(chatId, cancellationToken);
            var audio = OpusAudio.DecodeOgg(await api.DownloadFileAsync(voice.FileId, cancellationToken));
            text = await speechToText.TranscribeAsync(Wav.Write(audio), cancellationToken);
            if (text.Length == 0)
            {
                await api.SendMessageAsync(chatId, "Sesini anlayamadım, tekrar söyler misin?", cancellationToken: cancellationToken);
                return;
            }
            spoken = true;
        }
        if (text.Length == 0 && image is null)
            return;

        // Claude Code'un sorusuna yanıt (Telegram'da soru mesajını yanıtlayarak): doğrudan aynı kodlama oturumuna gider.
        if (message.ReplyToMessage is { } replied && replied.MessageId == state.CodingQuestionMessageId && text.Length > 0)
        {
            try
            {
                var job = await coding.ReplyAsync(text, cancellationToken: cancellationToken);
                state.CodingQuestionMessageId = null;
                await api.SendMessageAsync(chatId, $"💻 Cevabını #{job.Id} işine ilettim; Claude Code devam ediyor.", cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                await api.SendMessageAsync(chatId, ex.Message, cancellationToken: cancellationToken);
            }
            return;
        }

        // Hatırlatma çalarken "okudum/tamam": tekrar eden hatırlatmalar susar.
        if (IsAcknowledge(text) && await AcknowledgeRingingAsync(cancellationToken) > 0)
        {
            await api.SendMessageAsync(chatId, "Tamam, hatırlatmayı kapattım.", cancellationToken: cancellationToken);
            return;
        }

        await api.SendTypingAsync(chatId, cancellationToken);
        var conversation = await conversations.GetLatestAsync(cancellationToken) ?? await conversations.StartAsync(cancellationToken);
        var answer = "";
        await foreach (var e in orchestrator.SendAsync(conversation.Id, text, image, cancellationToken))
            if (e is AnswerCompleted done)
                answer = done.Text;

        var reply = ForTelegram(answer);
        await api.SendMessageAsync(chatId, spoken ? $"🎤 “{text}”\n\n{reply}" : reply, cancellationToken: cancellationToken);

        // (2026-10-30): Telegram'da sesli mesaj anlaşılır (Groq) ama cevap HER ZAMAN yazıyla (ElevenLabs kotası harcanmaz).
        if (VoiceRepliesOnTelegram && spoken && SpokenSummary(answer) is { Length: > 0 } summary)
        {
            try
            {
                var wav = await textToSpeech.SynthesizeAsync(summary, cancellationToken);
                if (wav.Length > 0)
                    await api.SendVoiceAsync(chatId, OpusAudio.EncodeOgg(Wav.Read(wav)), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Telegram sesli cevap gönderilemedi");
            }
        }
    }

    /// <summary>Telegram'dan gelen dosya Telegram koleksiyonuna eklenir (masaüstündeki /belgeler'den taşınabilir).</summary>
    private async Task<bool> AddDocumentAsync(long chatId, TgDocument file, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(file.FileName) ? "telegram-belgesi" : file.FileName;
        if (file.FileSize > MaxDownloadBytes)
        {
            await api.SendMessageAsync(chatId, $"{name} çok büyük: Telegram botları en fazla 20 MB indirebiliyor. Bilgisayardan pencereye sürükleyerek ekleyebilirsin.", cancellationToken: cancellationToken);
            return false;
        }

        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            await api.SendMessageAsync(chatId, $"📄 {name} okunuyor; resimli sayfalar varsa yazı tanıma birkaç dakika sürebilir.", silent: true, cancellationToken: cancellationToken);
        await api.SendTypingAsync(chatId, cancellationToken);
        var result = await documents.AddFileAsync(name, await api.DownloadFileAsync(file.FileId, cancellationToken), TelegramCollection, cancellationToken);
        var ok = result.Status is DocumentAddStatus.Added or DocumentAddStatus.Duplicate;
        await api.SendMessageAsync(chatId, (ok ? "📄 " : "") + result.Message, cancellationToken: cancellationToken);
        return ok;
    }

    internal const string TelegramCollection = "Telegram";

    private async Task HandleCallbackAsync(TgCallback callback, CancellationToken cancellationToken)
    {
        if (callback.Message is not { } message || message.Chat.Id != state.ChatId || callback.Data is not { } data)
            return;

        // Onay kartı düğmesi: "appr:ID:1" onay, "appr:ID:0" red (sadece eşleşmiş sohbetten).
        if (data.StartsWith("appr:", StringComparison.Ordinal) && data.Split(':') is [_, var idText, var choice] && long.TryParse(idText, out var approvalId))
        {
            var resolved = approvals.Resolve(approvalId, choice == "1");
            await api.AnswerCallbackAsync(callback.Id, !resolved ? "Bu onayın süresi dolmuş." : choice == "1" ? "Onaylandı." : "Reddedildi.", cancellationToken);
            await api.RemoveButtonsAsync(message.Chat.Id, message.MessageId, cancellationToken);
            return;
        }

        if (!data.StartsWith("ack:", StringComparison.Ordinal))
            return;
        var ok = long.TryParse(data[4..], out var id) && await reminders.AcknowledgeAsync(id, cancellationToken);
        await api.AnswerCallbackAsync(callback.Id, ok ? "Tamam, kapattım." : "Bu hatırlatma zaten kapanmış.", cancellationToken);
        await api.RemoveButtonsAsync(message.Chat.Id, message.MessageId, cancellationToken);
    }

    private async Task<int> AcknowledgeRingingAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var r in (await reminders.GetPendingAsync(cancellationToken)).Where(r => r.Status == ReminderStatus.Caliyor))
            if (await reminders.AcknowledgeAsync(r.Id, cancellationToken))
                count++;
        return count;
    }

    private static bool IsAcknowledge(string text) =>
        Core.Text.TurkishText.Normalize(text) is "okudum" or "tamam okudum" or "gordum" or "tamam" or "tamamdir" or "ok";

    /// <summary>Telegram düz metin gösteriyor: markdown işaretleri sadeleştirilir.</summary>
    internal static string ForTelegram(string markdown)
    {
        var text = Bold().Replace(Core.Chat.AnswerCleanup.PlainMath(markdown), "$1"); // Telegram LaTeX çizmez
        text = Heading().Replace(text, "");
        return text.Trim();
    }

    /// <summary>Sesli cevap: listeden önceki kısım, en fazla 3 cümle.</summary>
    internal static string SpokenSummary(string answer)
    {
        var cut = ListStart().Match(answer);
        var head = SpeechText.ForSpeech(cut.Success ? answer[..cut.Index] : answer);
        var sentences = SentenceEnd().Split(head).Where(s => s.Trim().Length > 0).Take(3);
        return string.Join(" ", sentences).Trim();
    }

    [GeneratedRegex(@"^/baglan\s+(\d{6})\s*$")]
    private static partial Regex PairCommand();

    [GeneratedRegex(@"\.(png|jpe?g|webp|bmp|gif)$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageNames();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?m)^#{1,6}\s*")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?m)^\s*([-*•+]\s|\d{1,2}[.)]\s|#{1,6}\s|\|)")]
    private static partial Regex ListStart();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();
}
