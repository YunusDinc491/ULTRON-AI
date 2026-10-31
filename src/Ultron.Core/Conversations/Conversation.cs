namespace Ultron.Core.Conversations;

/// <summary>Bir sohbet oturumu. Ham mesajlar kalıcı olarak saklanır (bilgi çıkarma ileride yeniden çalıştırılabilsin diye).</summary>
public sealed class Conversation
{
    /// <summary>Ölçüm/test konuşmalarının başlığı: Gelişim panosu bunları saymaz (2026-10-30).</summary>
    public const string TestTitle = "[ölçüm]";

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>UTC. SQLite DateTimeOffset'e göre sıralama yapamadığı için DateTime kullanılıyor.</summary>
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    public string? Title { get; set; }

    public List<Message> Messages { get; } = [];
}
