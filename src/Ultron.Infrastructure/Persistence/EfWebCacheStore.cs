using Microsoft.EntityFrameworkCore;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfWebCacheStore(IDbContextFactory<UltronDbContext> dbFactory) : IWebCacheStore
{
    public async Task<WebCacheEntry?> FindValidAsync(string cacheKey, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WebCache.AsNoTracking()
            .Where(e => e.CacheKey == cacheKey && (e.ExpiresAtUtc == null || e.ExpiresAtUtc > nowUtc))
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(WebCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.WebCache.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LogOutgoingAsync(string query, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OutgoingQueries.Add(new OutgoingQuery { Query = query, CreatedAtUtc = nowUtc });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutgoingQuery>> GetRecentOutgoingAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OutgoingQueries.AsNoTracking().OrderByDescending(q => q.Id).Take(limit).ToListAsync(cancellationToken);
    }
}
