using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Coding;
using Ultron.Host.Telegram;
using Ultron.Infrastructure.Telegram;

namespace Ultron.Host.Hubs;

/// <summary>
/// Kodlama işi: ilerleme masaüstüne; bitince, soru sorunca ya da limit dolunca kullanıcı uzaktaysa Telegram'a da.
/// Telegram'daki soru mesajını yanıtlamak (reply) cevabı Claude Code'a iletir.
/// </summary>
public sealed class CodingNotifier(
    IHubContext<ChatHub> hub,
    TelegramApi telegram,
    TelegramState telegramState,
    PresenceTracker presence,
    ILogger<CodingNotifier> logger) : ICodingNotifier
{
    public static CodingJobDto ToDto(CodingJob j) => new(j.Id, j.Title, j.Folder, j.Status.ToString(), j.Progress, j.LastMessage,
        j.Status == CodingStatus.Soruyor && j.LastMessage is { } m ? CodingService.QuestionText(m) : null, j.CostUsd, j.UpdatedAtUtc);

    public async Task NotifyAsync(CodingJob job, bool important, CancellationToken cancellationToken = default)
    {
        await hub.Clients.All.SendAsync(ChatHubContract.CodingUpdate, ToDto(job), cancellationToken);
        if (!important || telegramState.ChatId is not { } chatId || !presence.IsAway)
            return;

        var text = job.Status switch
        {
            CodingStatus.Soruyor => $"💻 #{job.Id} {job.Title} · Claude soruyor:\n{CodingService.QuestionText(job.LastMessage ?? "")}\n\n(Cevap için bu mesajı yanıtla.)",
            CodingStatus.Bitti => $"💻 #{job.Id} {job.Title} bitti:\n{job.LastMessage}",
            CodingStatus.Bekliyor => $"💻 #{job.Id} {job.Title}: Claude kullanım limiti doldu. Limit yenilenince \"devam et\" de.",
            CodingStatus.Hata => $"💻 #{job.Id} {job.Title} hata verdi:\n{job.LastMessage}",
            _ => null,
        };
        if (text is null)
            return;
        try
        {
            var sent = await telegram.SendMessageAsync(chatId, text, cancellationToken: cancellationToken);
            if (job.Status == CodingStatus.Soruyor)
                telegramState.CodingQuestionMessageId = sent.MessageId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TelegramException or TaskCanceledException)
        {
            logger.LogWarning("Kodlama bildirimi Telegram'a gönderilemedi: {Message}", ex.Message);
        }
    }
}
