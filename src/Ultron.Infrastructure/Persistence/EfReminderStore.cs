using Microsoft.EntityFrameworkCore;
using Ultron.Core.Reminders;
using Ultron.Core.Tools;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfReminderStore(IDbContextFactory<UltronDbContext> dbFactory) : IReminderStore
{
    public async Task<Reminder> AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(cancellationToken);
        return reminder;
    }

    public async Task<Reminder?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Reminders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Reminders.AsNoTracking()
            .Where(r => r.Status == ReminderStatus.Bekliyor || r.Status == ReminderStatus.Caliyor)
            .OrderBy(r => r.DueAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Reminders.AsNoTracking()
            .Where(r => (r.Status == ReminderStatus.Bekliyor && r.DueAtUtc <= nowUtc)
                     || (r.Status == ReminderStatus.Caliyor && r.NagAtUtc <= nowUtc))
            .OrderBy(r => r.DueAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var reminder = await db.Reminders.FirstOrDefaultAsync(
            r => r.Id == id && (r.Status == ReminderStatus.Bekliyor || r.Status == ReminderStatus.Caliyor), cancellationToken);
        if (reminder is null)
            return false;
        reminder.Status = ReminderStatus.Iptal;
        reminder.NagAtUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Reminders.Update(reminder);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderPreference>> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ReminderPreferences.AsNoTracking().OrderByDescending(p => p.UpdatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task SetPreferenceAsync(ReminderPreference preference, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ReminderPreferences.FirstOrDefaultAsync(p => p.Key == preference.Key, cancellationToken);
        if (existing is null)
            db.ReminderPreferences.Add(preference);
        else
        {
            existing.Text = preference.Text;
            existing.Priority = preference.Priority;
            existing.UpdatedAtUtc = preference.UpdatedAtUtc;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EfToolAuditLog(IDbContextFactory<UltronDbContext> dbFactory) : IToolAuditLog
{
    public async Task AddAsync(ToolAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ToolAudit.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolAuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ToolAudit.AsNoTracking().OrderByDescending(a => a.Id).Take(limit).ToListAsync(cancellationToken);
    }
}
