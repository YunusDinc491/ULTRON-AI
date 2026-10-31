namespace Ultron.Core.Memories;

public interface ICategoryStore
{
    /// <summary>Tüm kategoriler, sıralı.</summary>
    Task<IReadOnlyList<MemoryCategory>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Yeni kategori (aynı adda varsa o döner). Anahtar addan üretilir ("Araba & Motor" → "ArabaMotor").</summary>
    Task<MemoryCategory> AddAsync(string name, string description, string drawer, CancellationToken cancellationToken = default);

    Task UpdateAsync(string key, string name, string description, bool isCore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Boş, yerleşik olmayan kategoriyi siler; içindeki pasif (silinmiş) kayıtlar da kalıcı silinir (geri yüklenince yersiz kalmasınlar).
    /// Aktif kaydı olan ya da yerleşik kategori silinmez: false.
    /// </summary>
    Task<bool> DeleteIfEmptyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySuggestion>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Aynı adda kategori ya da bekleyen öneri varsa eklemez (model aynı öneriyi tekrar tekrar yapabilir).</summary>
    Task AddSuggestionAsync(string name, string description, string? example, CancellationToken cancellationToken = default);

    /// <summary>Kabul edilirse kategori eklenir ve döner; reddedilirse null.</summary>
    Task<MemoryCategory?> ResolveSuggestionAsync(long id, bool accept, CancellationToken cancellationToken = default);
}
