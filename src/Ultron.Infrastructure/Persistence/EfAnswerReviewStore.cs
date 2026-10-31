using Microsoft.EntityFrameworkCore;
using Ultron.Core.Learning;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfAnswerReviewStore(IDbContextFactory<UltronDbContext> dbFactory) : IAnswerReviewStore
{
    public async Task<AnswerReview> AddAsync(AnswerReview review, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.AnswerReviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task UpdateAsync(AnswerReview review, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.AnswerReviews.Update(review);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnswerReview?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AnswerReviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AnswerReview>> GetUnauditedAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AnswerReviews.AsNoTracking().Where(r => r.AuditedAtUtc == null).OrderBy(r => r.Id).Take(max).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnswerReview>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.AnswerReviews.AsNoTracking()
            .Where(r => r.Decision == ReviewDecision.Bekliyor && r.Correction != null && r.Verdict != null)
            .OrderByDescending(r => r.Id).ToListAsync(cancellationToken);
        return candidates.Where(r => r.IsPendingSuggestion).ToList();
    }

    public async Task<IReadOnlyList<AnswerReview>> GetDecidedAsync(ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AnswerReviews.AsNoTracking().Where(r => r.Decision == decision).OrderByDescending(r => r.Id).ToListAsync(cancellationToken);
    }
}
