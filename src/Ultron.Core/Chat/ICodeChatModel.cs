namespace Ultron.Core.Chat;

/// <summary>
/// Yazılım sorularında son cevabı yazan ayrı model (Faz 15 c, ör. qwen2.5-coder). Ayar boşsa kayıtlı değildir:
/// cevabı sohbet modeli yazar. Yönlendirme, kütüphane araması ve hafıza her zaman sohbet modelinde kalır.
/// </summary>
public interface ICodeChatModel
{
    string Name { get; }

    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
