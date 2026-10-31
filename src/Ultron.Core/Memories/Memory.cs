namespace Ultron.Core.Memories;

/// <summary>"Kullanıcı Dosyası"ndaki tek bir kalıcı bilgi.</summary>
public sealed class Memory
{
    public long Id { get; init; }

    /// <summary>Kategori anahtarı (<see cref="MemoryCategory.Key"/>): "OzelHayat".</summary>
    public required string Category { get; set; }

    /// <summary>Üçüncü şahıs, kısa cümle: "Kedisinin adı Pamuk".</summary>
    public required string Content { get; set; }

    /// <summary>Content'in anlam vektörü; arama bununla yapılır.</summary>
    public float[] Embedding { get; set; } = [];

    /// <summary>Bilginin çıkarıldığı mesaj (iz sürebilmek için).</summary>
    public long? SourceMessageId { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Silinen bilgi kaybolmaz, pasifleşir: yanlışlıkla silineni geri getirebilelim.</summary>
    public bool IsActive { get; set; } = true;
}
