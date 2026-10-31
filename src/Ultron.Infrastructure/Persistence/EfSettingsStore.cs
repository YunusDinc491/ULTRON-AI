using Microsoft.EntityFrameworkCore;
using Ultron.Core.Calendar;
using Ultron.Core.Settings;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfSettingsStore(IDbContextFactory<UltronDbContext> dbFactory) : ISettingsStore
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
    }

    public async Task SetAsync(string key, string value, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.UserSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (existing is null)
            db.UserSettings.Add(new UserSetting { Key = key, Value = value, UpdatedAtUtc = nowUtc });
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = nowUtc;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EfCalendarStore(IDbContextFactory<UltronDbContext> dbFactory) : ICalendarStore
{
    public async Task<CalendarEvent> AddAsync(CalendarEvent item, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.CalendarEvents.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CalendarEvents.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(CalendarEvent item, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.CalendarEvents.Update(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CalendarEvents.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
