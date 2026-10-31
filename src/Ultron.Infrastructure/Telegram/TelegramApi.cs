using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ultron.Infrastructure.Telegram;

/// <summary>appsettings "Telegram" bölümü. Token user-secrets'ta (kullanıcı kendisi kaydeder, repoda ve Claude'da yok).</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string? BotToken { get; set; }

    public string ApiBase { get; set; } = "https://api.telegram.org";

    /// <summary>Uzun yoklama: Telegram yeni mesaj gelene kadar bağlantıyı bu kadar saniye açık tutar (internete açılmaya gerek yok).</summary>
    public int PollSeconds { get; set; } = 25;

    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
}

// Telegram Bot API'nin kullandığımız kadarı (https://core.telegram.org/bots/api). Alan adları snake_case.
public sealed record TgResponse<T>(bool Ok, T? Result, string? Description);
public sealed record TgUser(long Id, string? FirstName, string? Username);
public sealed record TgChat(long Id);
public sealed record TgVoice(string FileId, int Duration);
public sealed record TgDocument(string FileId, string? FileName, long? FileSize);
public sealed record TgPhoto(string FileId, int Width, int Height);
public sealed record TgMessage(long MessageId, TgChat Chat, TgUser? From, string? Text, TgVoice? Voice, TgDocument? Document = null, string? Caption = null, TgPhoto[]? Photo = null, TgMessage? ReplyToMessage = null);
public sealed record TgCallback(string Id, TgUser From, TgMessage? Message, string? Data);
public sealed record TgUpdate(long UpdateId, TgMessage? Message, TgCallback? CallbackQuery);
public sealed record TgFile(string FileId, string? FilePath);

/// <summary>Telegram Bot API istemcisi (ham HTTP; Telegram.Bot kütüphanesine gerek yok, kullandığımız birkaç uç nokta).</summary>
public sealed class TelegramApi(HttpClient http, IOptions<TelegramOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string Url(string method) => $"{options.Value.ApiBase}/bot{options.Value.BotToken}/{method}";

    private async Task<T> CallAsync<T>(string method, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(Url(method), body, Json, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TgResponse<T>>(Json, cancellationToken);
        if (result is not { Ok: true, Result: not null })
            throw new TelegramException($"{method}: {result?.Description ?? response.StatusCode.ToString()}");
        return result.Result;
    }

    public Task<TgUser> GetMeAsync(CancellationToken cancellationToken) => CallAsync<TgUser>("getMe", new { }, cancellationToken);

    public Task<TgUpdate[]> GetUpdatesAsync(long offset, CancellationToken cancellationToken) =>
        CallAsync<TgUpdate[]>("getUpdates", new
        {
            offset,
            timeout = options.Value.PollSeconds,
            allowed_updates = new[] { "message", "callback_query" },
        }, cancellationToken);

    /// <param name="ackReminderId">Varsa mesaja [✅ Okudum] düğmesi eklenir.</param>
    /// <param name="silent">Telefonda sessiz bildirim (sessiz saatler).</param>
    public Task<TgMessage> SendMessageAsync(long chatId, string text, long? ackReminderId = null, bool silent = false, CancellationToken cancellationToken = default) =>
        CallAsync<TgMessage>("sendMessage", new
        {
            chat_id = chatId,
            text = text.Length > 4000 ? text[..4000] + " …" : text,
            disable_notification = silent ? true : (bool?)null,
            reply_markup = ackReminderId is { } id
                ? new { inline_keyboard = new[] { new[] { new { text = "✅ Okudum", callback_data = $"ack:{id}" } } } }
                : null,
        }, cancellationToken);

    /// <summary>Onay kartı: [✅ Onayla] [✖ Reddet] düğmeleri (callback "appr:ID:1" / "appr:ID:0").</summary>
    public Task<TgMessage> SendApprovalAsync(long chatId, string text, long approvalId, CancellationToken cancellationToken) =>
        CallAsync<TgMessage>("sendMessage", new
        {
            chat_id = chatId,
            text,
            reply_markup = new { inline_keyboard = new[] { new[] { new { text = "✅ Onayla", callback_data = $"appr:{approvalId}:1" }, new { text = "✖ Reddet", callback_data = $"appr:{approvalId}:0" } } } },
        }, cancellationToken);

    public Task<bool> SendTypingAsync(long chatId, CancellationToken cancellationToken) =>
        CallAsync<bool>("sendChatAction", new { chat_id = chatId, action = "typing" }, cancellationToken);

    public Task<bool> AnswerCallbackAsync(string callbackId, string text, CancellationToken cancellationToken) =>
        CallAsync<bool>("answerCallbackQuery", new { callback_query_id = callbackId, text }, cancellationToken);

    /// <summary>"Okudum"dan sonra düğme kaldırılır.</summary>
    public Task<JsonNode> RemoveButtonsAsync(long chatId, long messageId, CancellationToken cancellationToken) =>
        CallAsync<JsonNode>("editMessageReplyMarkup", new { chat_id = chatId, message_id = messageId, reply_markup = new { inline_keyboard = Array.Empty<object>() } }, cancellationToken);

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var file = await CallAsync<TgFile>("getFile", new { file_id = fileId }, cancellationToken);
        return await http.GetByteArrayAsync($"{options.Value.ApiBase}/file/bot{options.Value.BotToken}/{file.FilePath}", cancellationToken);
    }

    /// <summary>Sesli mesaj (OGG/Opus).</summary>
    public async Task SendVoiceAsync(long chatId, byte[] ogg, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "chat_id" },
            { new ByteArrayContent(ogg) { Headers = { ContentType = new("audio/ogg") } }, "voice", "ultron.ogg" },
        };
        using var response = await http.PostAsync(Url("sendVoice"), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TelegramException("sendVoice: " + await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

public sealed class TelegramException(string message) : Exception(message);
