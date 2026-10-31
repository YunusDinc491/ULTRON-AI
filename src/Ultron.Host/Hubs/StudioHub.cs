using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Studio;

namespace Ultron.Host.Hubs;

public static class StudioMapper
{
    public static StudioProjectDto ToDto(StudioProject p) => new(p.Id, p.Name, p.UpdatedAtUtc.ToLocalTime(), p.CurrentVersionId);

    public static StudioVersionDto ToDto(StudioVersion v) =>
        new(v.Id, v.Number, v.Request, v.Code, v.Explanation, v.Source.ToString(), v.CreatedAtUtc.ToLocalTime(), v.ChecksPassed, v.CheckReport, v.Rating, v.Feedback);

    public static StudioMessageDto ToDto(StudioMessage m) => new(m.Id, m.FromUser, m.Content, m.VersionId, m.CreatedAtUtc.ToLocalTime());

    public static StudioEventDto ToDto(StudioEvent e) => e switch
    {
        StudioStarted s => new StudioEventDto("started", Recipes: s.Recipes, KnownPattern: s.KnownPattern),
        StudioChunk c => new StudioEventDto("chunk", c.Text),
        StudioCompleted c => new StudioEventDto("completed", c.Explanation, Version: ToDto(c.Version)),
        StudioFailed f => new StudioEventDto("failed", f.Message),
        _ => new StudioEventDto("unknown"),
    };
}

/// <summary>Claude Code'un bileşen işi ve bileşen denetimi: masaüstündeki İş arayüzüne.</summary>
public sealed class HubStudioNotifier(IHubContext<ChatHub> hub) : IStudioNotifier
{
    public Task ClaudeProgressAsync(long projectId, string status, bool finished, StudioVersion? version, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.StudioUpdate, new StudioUpdateDto(projectId, status, finished, version is null ? null : StudioMapper.ToDto(version)), cancellationToken);

    public Task ReviewedAsync(int reviewed, int recipes, int rules, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.StudioReviewed, reviewed, recipes, rules, cancellationToken);
}
