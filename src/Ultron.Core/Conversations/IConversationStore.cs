using Ultron.Core.Chat;

namespace Ultron.Core.Conversations;

/// <summary>Konuşmaların kalıcı deposu. Core, verinin SQLite'ta mı başka yerde mi durduğunu bilmez.</summary>
public interface IConversationStore
{
    Task<Conversation> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>En son başlatılan konuşmayı mesajlarıyla (eskiden yeniye) döndürür; hiç yoksa null.</summary>
    Task<Conversation?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Bir konuşmanın son <paramref name="limit"/> mesajı, eskiden yeniye.</summary>
    Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int limit, CancellationToken cancellationToken = default);

    Task<Message> AddMessageAsync(Guid conversationId, Role role, string content, CancellationToken cancellationToken = default);

    /// <summary>Konuşmaya başlık verir (ör. "[ölçüm]": ölçüm/test konuşmaları Gelişim panosuna sayılmaz).</summary>
    Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
