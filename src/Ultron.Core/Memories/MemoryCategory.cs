namespace Ultron.Core.Memories;

/// <summary>
/// Bir hafıza kategorisi (çekmecedeki klasör). Her kategori dört çekmeceden birindedir (<see cref="MemoryDrawers"/>).
/// Tek yerleşik kategori Sağlık; gerisini Ultron konuşmalara göre kendisi açar (tasarım kararı, 2026-10-28), kullanıcı da ekleyebilir.
/// </summary>
public sealed class MemoryCategory
{
    /// <summary>Kalıcı anahtar (ASCII, boşluksuz): "EvcilHayvanlar". Hafıza kayıtları buna bağlı; ad değişse de değişmez.</summary>
    public required string Key { get; init; }

    /// <summary>Görünen ad: "Evcil hayvanlar".</summary>
    public required string Name { get; set; }

    /// <summary>Modele ne tür bilgilerin bu kategoriye girdiğini anlatan açıklama.</summary>
    public required string Description { get; set; }

    /// <summary>Çekmece anahtarı: <see cref="MemoryDrawers.Patron"/>, Is, Okul ya da Bilgi.</summary>
    public string Drawer { get; set; } = MemoryDrawers.Patron;

    /// <summary>Çekirdek profil: bu kategorideki kayıtlar her soruda bağlama girer (ör. Sağlık/alerji).</summary>
    public bool IsCore { get; set; }

    public bool IsBuiltIn { get; init; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Modelin önerdiği, kullanıcı onayı bekleyen yeni kategori.</summary>
public sealed class CategorySuggestion
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>Öneriye yol açan bilgi (kullanıcı neden önerildiğini görsün).</summary>
    public string? Example { get; init; }

    public SuggestionStatus Status { get; set; } = SuggestionStatus.Pending;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public enum SuggestionStatus
{
    Pending,
    Accepted,
    Rejected,
}

public sealed record MemoryDrawer(string Key, string Name, string Description);

/// <summary>
/// Hafızanın dört çekmecesi (tasarım kararı, 2026-10-28). Amaç: Ultron soru sorulmasa da bu başlıklarda biriktirdikleriyle
/// daha iyi tavsiye verebilsin. Çekmeceler sabit, içlerindeki klasörleri (kategorileri) Ultron açar.
/// </summary>
public static class MemoryDrawers
{
    public const string Patron = "Patron";
    public const string Is = "Is";
    public const string Okul = "Okul";
    public const string Bilgi = "Bilgi";

    public static IReadOnlyList<MemoryDrawer> All { get; } =
    [
        new(Patron, "PATRON", "Kullanıcının kendisi ve özel hayatı: aile, arkadaşlar, sevgili, sağlık, ev, evcil hayvan, eşyalar, zevkler, yemek, hobiler, spor, rutinler, para, kişisel hedefler ve kararlar"),
        new(Is, "İŞ", "Kullanıcının yazılım işleri: web ve mobil full stack uygulamalar, projeleri, kullandığı ve öğrendiği teknolojiler, kodlama tercihleri, staj, kariyer, iş hedefleri"),
        new(Okul, "OKUL", "Kullanıcının okulu: bölümü, dersleri, hocaları, ders programı, sınavları, notları, ödevleri"),
        new(Bilgi, "BİLGİ", "Kullanıcıya dair OLMAYAN genel bilgiler: kullanıcının Ultron'a öğrettikleri ve Ultron'un araştırıp öğrendikleri"),
    ];

    public static MemoryDrawer? Find(string? key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Yerleşik kategoriler.</summary>
public static class MemoryCategories
{
    public const string Saglik = "Saglik";

    /// <summary>İlk kurulumda veritabanına yazılan liste (migration seed). Sağlık hazır ve çekirdek: alerji gibi bilgiler her soruda akılda.</summary>
    public static IReadOnlyList<MemoryCategory> BuiltIn { get; } =
    [
        new()
        {
            Key = Saglik,
            Name = "Sağlık",
            Description = "Sağlık durumu, alerji, ilaç, hastalık",
            Drawer = MemoryDrawers.Patron,
            IsCore = true,
            IsBuiltIn = true,
            SortOrder = 1,
            CreatedAtUtc = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), // seed sabit olmalı (migration)
        },
    ];
}
