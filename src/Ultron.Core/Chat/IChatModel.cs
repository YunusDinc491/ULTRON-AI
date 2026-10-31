using Ultron.Core.Tools;

namespace Ultron.Core.Chat;

/// <summary>
/// Sohbet modeli soyutlaması. Core, modelin Ollama'da mı başka yerde mi çalıştığını bilmez.
/// </summary>
public interface IChatModel
{
    /// <summary>Cevabı üretildikçe parça parça döndürür (streaming).</summary>
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen JSON şemasına uymak zorunda olan tek parça cevap. Bilgi çıkarma, karar verme gibi
    /// kullanıcının görmediği iç işler için.
    /// </summary>
    Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yaratıcı yazım (fal yorumu): sıcaklık ve tekrar cezası ayarlı akış; aynı klişelere takılmasın. Desteklemeyen model
    /// normal akışa düşer.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, double temperature, double repeatPenalty, CancellationToken cancellationToken = default) =>
        StreamAsync(messages, cancellationToken);

    /// <summary>
    /// Uzun JSON için (quiz, adım adım çözüm): varsayılan uzunluk sınırı (900 token) yerine <paramref name="maxTokens"/>.
    /// Sınırı desteklemeyen model normal çağrıya düşer.
    /// </summary>
    Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, int maxTokens, CancellationToken cancellationToken = default) =>
        CompleteJsonAsync(messages, jsonSchema, cancellationToken);

    /// <summary>
    /// Araç kullanabilen tek parça cevap: model ya araç çağırır (<see cref="ToolTurn.Calls"/>) ya da son cevabı yazar.
    /// </summary>
    Task<ToolTurn> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default);
}
