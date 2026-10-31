using Microsoft.Extensions.Options;
using Ultron.Contracts;
using Ultron.Core.Memories;
using Ultron.Core.Reminders;
using Ultron.Core.Tools;
using Ultron.Core.Web;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Web;

namespace Ultron.Host.Status;

/// <summary>Command Center'ın durum panelleri için anlık özet.</summary>
public sealed class StatusService(
    IMemoryStore memories,
    ICategoryStore categories,
    IWebCacheStore webCache,
    IToolAuditLog toolAudit,
    ReminderService reminders,
    Ultron.Infrastructure.Telegram.TelegramState telegram,
    IOptions<OllamaOptions> ollama,
    IOptions<WebOptions> web,
    IHttpClientFactory httpFactory,
    Ultron.Core.Computer.ComputerPermissions permissions,
    Ultron.Core.Coding.CodingService coding,
    Ultron.Core.Briefing.WakeForBriefingService wake,
    Ultron.Core.Backup.BackupService backups,
    Ultron.Infrastructure.Services.DependencyStatus dependencies,
    Ultron.Core.Library.LibraryService library,
    TimeProvider time)
{
    public async Task<StatusDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var active = await memories.GetActiveAsync(cancellationToken);
        var queries = await webCache.GetRecentOutgoingAsync(6, cancellationToken);
        // Durum panelinde "PATRON › Aile" biçiminde: hangi çekmecede neler biriktiği bir bakışta görünsün.
        var names = (await categories.GetAllAsync(cancellationToken)).ToDictionary(c => c.Key, c => $"{Ultron.Core.Memories.MemoryDrawers.Find(c.Drawer)?.Name ?? c.Drawer} › {c.Name}");

        return new StatusDto(
            ollama.Value.ChatModel,
            ollama.Value.EmbeddingModel,
            active.Count,
            active.GroupBy(m => names.GetValueOrDefault(m.Category, m.Category)).ToDictionary(g => g.Key, g => g.Count()),
            await IsSearxngUpAsync(cancellationToken),
            queries.Select(q => new OutgoingQueryDto(q.Query, q.CreatedAtUtc)).ToList(),
            time.GetUtcNow().UtcDateTime,
            (await toolAudit.GetRecentAsync(6, cancellationToken)).Select(a => new ToolAuditDto(a.Tool, a.Arguments, a.Result, a.Ok, a.AtUtc, a.Approval, a.Level.ToString())).ToList(),
            (await reminders.GetPendingAsync(cancellationToken)).Take(10)
                .Select(r => new ReminderDto(r.Id, r.Text, reminders.ToLocal(r.DueAtUtc), ReminderSchedule.Describe(r.Recurrence))).ToList(),
            telegram.Describe(),
            await PermissionsAsync(cancellationToken),
            (await coding.AgentStatusAsync(cancellationToken)).Message,
            (await coding.RecentAsync(3, cancellationToken)).Select(Ultron.Host.Hubs.CodingNotifier.ToDto).ToList(),
            await wake.DescribeAsync(cancellationToken),
            await backups.DescribeAsync(cancellationToken),
            dependencies.Describe(),
            await library.DescribeAsync(cancellationToken));
    }

    private async Task<PermissionsDto> PermissionsAsync(CancellationToken cancellationToken)
    {
        var p = await permissions.GetAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        return new PermissionsDto(
            p.Apps.Select(a => a.Name).Distinct().ToList(),
            p.Folders,
            p.Grants.Select(g => $"{g.Scope} · {(int)Math.Ceiling((g.UntilUtc - now).TotalMinutes)} dk").ToList(),
            p.SharedMemory);
    }

    private async Task<bool> IsSearxngUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(2);
            using var response = await http.GetAsync(new Uri(web.Value.SearxngUrl, "healthz"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
