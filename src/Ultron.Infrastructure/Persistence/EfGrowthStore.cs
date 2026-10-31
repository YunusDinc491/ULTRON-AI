using Microsoft.EntityFrameworkCore;
using Ultron.Core.Chat;
using Ultron.Core.Growth;
using Ultron.Core.Learning;
using Ultron.Core.Studio;

namespace Ultron.Infrastructure.Persistence;

/// <summary>Gelişim panosu: diğer tabloları sadece okur, gece raporları ve quiz sonuçlarını yazar.</summary>
internal sealed class EfGrowthStore(IDbContextFactory<UltronDbContext> dbFactory) : IGrowthStore
{
    public async Task AddReportAsync(NightlyReport report, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.NightlyReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddQuizResultAsync(QuizResult result, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.QuizResults.Add(result);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GrowthRaw> LoadAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Ölçüm/test konuşmaları sayılmaz (2026-10-30: Claude'un ölçümleri kullanıcının istatistiklerini şişiriyordu).
        var tests = db.Conversations.Where(c => c.Title == Ultron.Core.Conversations.Conversation.TestTitle).Select(c => c.Id);
        var messages = (await db.Messages.AsNoTracking().Where(m => m.CreatedAtUtc >= sinceUtc && (m.Role == Role.User || m.Role == Role.Assistant) && !tests.Contains(m.ConversationId))
            .Select(m => new { m.CreatedAtUtc, m.Role }).ToListAsync(cancellationToken)).Select(m => (m.CreatedAtUtc, m.Role == Role.User)).ToList();
        var components = (await db.StudioVersions.AsNoTracking().Where(v => v.CreatedAtUtc >= sinceUtc)
            .Select(v => new { v.CreatedAtUtc, v.Source, v.Rating }).ToListAsync(cancellationToken)).Select(v => (v.CreatedAtUtc, v.Source == StudioSource.Claude, v.Rating)).ToList();
        var lesson = (await db.LessonMessages.AsNoTracking().Where(m => !m.FromUser && m.CreatedAtUtc >= sinceUtc)
            .Select(m => new { m.CreatedAtUtc, m.Author, m.Unsure }).ToListAsync(cancellationToken)).Select(m => (m.CreatedAtUtc, m.Author, m.Unsure)).ToList();
        var quizzes = await db.QuizResults.AsNoTracking().Where(q => q.AtUtc >= sinceUtc).ToListAsync(cancellationToken);
        var reviews = (await db.AnswerReviews.AsNoTracking().Where(r => r.AuditedAtUtc != null && r.AuditedAtUtc >= sinceUtc)
            .Select(r => new { r.AuditedAtUtc, r.Decision }).ToListAsync(cancellationToken)).Select(r => (r.AuditedAtUtc!.Value, r.Decision == ReviewDecision.Onaylandi)).ToList();
        var reports = await db.NightlyReports.AsNoTracking().OrderByDescending(r => r.Id).Take(40).ToListAsync(cancellationToken);
        var sessions = await db.LessonSessions.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var topics = (await db.WeakTopics.AsNoTracking().ToListAsync(cancellationToken))
            .Select(t => (sessions.GetValueOrDefault(t.SessionId, "?"), t.Topic, t.Misses, t.Hits)).ToList();
        var recipes = await db.StudioRecipes.AsNoTracking().Where(r => r.IsActive).Select(r => r.Kind).ToListAsync(cancellationToken);
        return new GrowthRaw(messages, components, lesson, quizzes, reviews, reports, topics, recipes.Count, recipes.Count(k => k != RecipeKind.Hazir));
    }
}
