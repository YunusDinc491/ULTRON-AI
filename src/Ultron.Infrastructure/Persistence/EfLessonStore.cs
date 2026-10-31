using Microsoft.EntityFrameworkCore;
using Ultron.Core.Lessons;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfLessonStore(IDbContextFactory<UltronDbContext> dbFactory) : ILessonStore
{
    public async Task<LessonSession> AddSessionAsync(LessonSession session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task UpdateSessionAsync(LessonSession session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonSessions.Update(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LessonSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<LessonSession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonSessions.AsNoTracking().OrderByDescending(s => s.UpdatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteSessionAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.LessonMessages.Where(m => m.SessionId == id).ExecuteDeleteAsync(cancellationToken);
        await db.LessonNotes.Where(n => n.SessionId == id).ExecuteDeleteAsync(cancellationToken);
        await db.LessonMaterials.Where(m => m.SessionId == id).ExecuteDeleteAsync(cancellationToken);
        await db.WeakTopics.Where(t => t.SessionId == id).ExecuteDeleteAsync(cancellationToken);
        return await db.LessonSessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<LessonMessage> AddMessageAsync(LessonMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task UpdateMessageAsync(LessonMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonMessages.Update(message);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LessonMessage>> GetMessagesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMessages.AsNoTracking().Where(m => m.SessionId == sessionId).OrderBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<LessonMessage?> GetMessageAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<LessonMessage>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMessages.AsNoTracking()
            .Where(m => !m.FromUser && m.ReviewedAtUtc == null && m.Author != "claude")
            .OrderByDescending(m => m.Id).Take(max).ToListAsync(cancellationToken);
    }

    public async Task<LessonNote> AddNoteAsync(LessonNote note, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return note;
    }

    public async Task<IReadOnlyList<LessonNote>> GetNotesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonNotes.AsNoTracking().Where(n => n.SessionId == sessionId && n.IsActive).OrderBy(n => n.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> DeactivateNoteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonNotes.Where(n => n.Id == id && n.IsActive).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsActive, false), cancellationToken) > 0;
    }

    public async Task<LessonMaterial> AddMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonMaterials.Add(material);
        await db.SaveChangesAsync(cancellationToken);
        return material;
    }

    public async Task UpdateMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.LessonMaterials.Update(material);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LessonMaterial?> GetMaterialAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<LessonMaterial>> GetMaterialsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMaterials.AsNoTracking().Where(m => m.SessionId == sessionId).OrderByDescending(m => m.UpdatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteMaterialAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LessonMaterials.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<WeakTopic>> GetTopicsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WeakTopics.AsNoTracking().Where(t => t.SessionId == sessionId).OrderByDescending(t => t.Misses - t.Hits).ToListAsync(cancellationToken);
    }

    public async Task SaveTopicAsync(WeakTopic topic, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (topic.Id == 0)
            db.WeakTopics.Add(topic);
        else
            db.WeakTopics.Update(topic);
        await db.SaveChangesAsync(cancellationToken);
    }
}
