namespace Ultron.Core.Embeddings;

/// <summary>
/// Metnin ne için vektöre çevrildiği. Qwen3-Embedding soruya bir görev talimatı ister, belgeye istemez;
/// talimatsız soru alakalı ile alakasızı daha kötü ayırıyor.
/// </summary>
public enum EmbeddingPurpose
{
    /// <summary>Saklanacak bilgi (hafıza kaydı, belge parçası).</summary>
    Document,

    /// <summary>Arama sorusu (kullanıcının mesajı).</summary>
    Query,
}

/// <summary>Metni anlamını temsil eden bir sayı dizisine (vektöre) çevirir.</summary>
public interface IEmbeddingModel
{
    /// <summary>Her metin için bir vektör; sıra korunur.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default);
}
