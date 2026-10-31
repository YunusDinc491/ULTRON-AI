using Ultron.Core.Conversations;

namespace Ultron.Core.Chat;

/// <summary>Modele gönderilecek mesaj listesini hazırlar.</summary>
public static class ChatContext
{
    /// <summary>
    /// System mesajı + son <paramref name="maxMessages"/> mesaj.
    /// Konuşma sonsuza kadar uzayabilir ama modelin bağlamı (num_ctx) sınırlı;
    /// sınır aşılırsa Ollama en eskileri sessizce keser. Pencereyi kendimiz belirleriz.
    /// </summary>
    public static List<ChatMessage> Build(string systemPrompt, IEnumerable<Message> history, int maxMessages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessages);

        var messages = new List<ChatMessage> { new(Role.System, systemPrompt) };
        messages.AddRange(history
            .Where(m => m.Role != Role.System)
            .TakeLast(maxMessages)
            .Select(m => new ChatMessage(m.Role, m.Content)));
        return messages;
    }
}
