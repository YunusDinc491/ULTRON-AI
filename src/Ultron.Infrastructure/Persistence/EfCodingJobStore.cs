using Microsoft.EntityFrameworkCore;
using Ultron.Core.Coding;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfCodingJobStore(IDbContextFactory<UltronDbContext> dbFactory) : ICodingJobStore
{
    public async Task<CodingJob> AddAsync(CodingJob job, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.CodingJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task UpdateAsync(CodingJob job, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.CodingJobs.Update(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CodingJob?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodingJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CodingJob>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodingJobs.AsNoTracking().OrderByDescending(j => j.Id).Take(limit).ToListAsync(cancellationToken);
    }
}
