using Microsoft.EntityFrameworkCore;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;

namespace Ultron.Infrastructure.Persistence;

/// <summary>
/// Her işlemde kısa ömürlü bir DbContext açar (factory). İleride arka plan servisleri
/// aynı anda çalışacağı için tek bir uzun ömürlü DbContext paylaşmak güvenli olmaz.
/// </summary>
internal sealed class EfConversationStore(IDbContextFactory<UltronDbContext> dbFactory) : IConversationStore
{
    public async Task<Conversation> StartAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversation = new Conversation();
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<Conversation?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.Id))
            .OrderByDescending(c => c.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var latestFirst = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        latestFirst.Reverse();
        return latestFirst;
    }

    public async Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, title), cancellationToken);
    }

    public async Task<Message> AddMessageAsync(Guid conversationId, Role role, string content, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var message = new Message { ConversationId = conversationId, Role = role, Content = content };
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }
}
