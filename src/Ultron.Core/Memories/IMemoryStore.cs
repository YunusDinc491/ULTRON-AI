namespace Ultron.Core.Memories;

public interface IMemoryStore
{
    /// <summary>Tüm aktif kayıtlar (vektörleriyle). Kayıt sayısı binlere çıkana kadar bellekte aramak yeterince hızlı.</summary>
    Task<IReadOnlyList<Memory>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalleştirilmiş önekleri (<see cref="KeywordQuery"/>) içeren aktif kayıtlar, en iyi eşleşen önce.
    /// Embedding'in kaçırdığı özel isimler ("Pamuk", "ASELSAN") için.
    /// </summary>
    Task<IReadOnlyList<Memory>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, int limit, CancellationToken cancellationToken = default);

    Task<Memory> AddAsync(Memory memory, CancellationToken cancellationToken = default);

    Task UpdateAsync(long id, string content, string category, float[] embedding, CancellationToken cancellationToken = default);

    Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Silinmiş (pasif) kaydı geri getirir: "geri al".</summary>
    Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Son silinen kayıtlar, en yeni önce (hafıza panelindeki "Silinenler").</summary>
    Task<IReadOnlyList<Memory>> GetRecentlyDeactivatedAsync(int limit, CancellationToken cancellationToken = default);
}
