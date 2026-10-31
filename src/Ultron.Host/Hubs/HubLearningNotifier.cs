using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Learning;

namespace Ultron.Host.Hubs;

/// <summary>Gece denetimi bitti: masaüstüne haber (tepsi bildirimi + Öğrenme paneli sayacı).</summary>
public sealed class HubLearningNotifier(IHubContext<ChatHub> hub) : ILearningNotifier
{
    public Task NotifyAuditedAsync(int reviewed, int learned, int pending, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.LearningReady, reviewed, learned, pending, cancellationToken);
}
