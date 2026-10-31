using Ultron.Core.Chat;

namespace Ultron.Core.Conversations;

/// <summary>Veritabanında saklanan tek bir sohbet mesajı.</summary>
public sealed class Message
{
    public long Id { get; init; }

    public Guid ConversationId { get; init; }

    public Role Role { get; init; }

    public required string Content { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
