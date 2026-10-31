using Microsoft.EntityFrameworkCore;
using Ultron.Core.Pro;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfProAnswerStore(IDbContextFactory<UltronDbContext> dbFactory) : IProAnswerStore
{
    public async Task<ProAnswer> AddAsync(ProAnswer answer, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ProAnswers.Add(answer);
        await db.SaveChangesAsync(cancellationToken);
        return answer;
    }

    public async Task UpdateAsync(ProAnswer answer, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ProAnswers.Update(answer);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProAnswer>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProAnswers.AsNoTracking().Where(a => a.IsActive).OrderByDescending(a => a.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProAnswers.Where(a => a.Id == id && a.IsActive).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), cancellationToken) > 0;
    }
}
