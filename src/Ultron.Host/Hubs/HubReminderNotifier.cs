using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Briefing;
using Ultron.Core.Reminders;

namespace Ultron.Host.Hubs;

/// <summary>Zamanı gelen hatırlatmayı ve sabah brifingini bağlı tüm istemcilere (masaüstü; ileride Telegram köprüsü) iletir.</summary>
public sealed class HubReminderNotifier(IHubContext<ChatHub> hub, ILogger<HubReminderNotifier> logger) : IReminderNotifier, IBriefingNotifier
{
    public async Task NotifyAsync(ReminderDue due, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Hatırlatma gönderiliyor #{Id}: {Text}", due.Id, due.Text);
        await hub.Clients.All.SendAsync(ChatHubContract.ReminderDue,
            new ReminderDueDto(due.Id, due.Text, due.DueAtLocal, due.Missed, due.Spoken, due.Priority.ToString(), due.Repeat, due.Quiet), cancellationToken);
    }

    public async Task NotifyBriefingAsync(string text, bool late, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Brifing gönderiliyor{Late}", late ? " (telafi)" : "");
        await hub.Clients.All.SendAsync(ChatHubContract.Briefing, new BriefingDto(text, late), cancellationToken);
    }
}

/// <summary>Açılışta başlatılamayan servis (Ollama, SearXNG): masaüstünde tepsi bildirimi.</summary>
public sealed class HubServiceWarningNotifier(IHubContext<ChatHub> hub) : Ultron.Infrastructure.Services.IServiceWarningNotifier
{
    public Task NotifyServiceWarningAsync(string text, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.ServiceWarning, text, cancellationToken);
}

/// <summary>Yeni hafıza kategorisi: masaüstünde sohbete kısa not.</summary>
public sealed class HubCategoryNotifier(IHubContext<ChatHub> hub) : Ultron.Core.Memories.ICategoryNotifier
{
    public Task NotifyCategoryCreatedAsync(Ultron.Core.Memories.MemoryCategory category, string example, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.CategoryCreated, category.Name, example, cancellationToken);
}
