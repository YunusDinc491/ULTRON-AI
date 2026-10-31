namespace Ultron.Core.Memories;

/// <summary>
/// appsettings.json içindeki "Memory" bölümü. Eşikler embedding modeline özgüdür:
/// qwen3-embedding:0,6b ölçümü (2026-10-28, ILERLEME.md): soru↔doğru kayıt 0,36-0,69, soru↔yanlış kayıt ≤0,44;
/// aynı konudaki iki kayıt 0,73-0,89, farklı konular ≤0,50.
/// </summary>
public sealed class MemoryOptions
{
    public const string SectionName = "Memory";

    /// <summary>Bir soruya en fazla kaç hafıza kaydı eklenir (anlamsal arama).</summary>
    public int RecallTopK { get; set; } = 6;

    /// <summary>
    /// Soru↔kayıt benzerliği bunun altındaysa alakasız sayılır. 0,35'te alakasız kayıtlar da geliyordu (gece sınavı: EF Core cevabına
    /// "Flutter uygulaman için…" eklendi); alakasızların en yükseği 0,44 → 0,45. Kaçan alakalı kayıtları anahtar kelime araması yakalar.
    /// </summary>
    public double RecallMinScore { get; set; } = 0.45;

    /// <summary>Kayıt↔kayıt benzerliği bunun üstündeyse "çelişiyor mu?" diye modele sorulur. Ölçüm: aynı konu ≥0,73, farklı ≤0,50.</summary>
    public double ConflictCandidateMinScore { get; set; } = 0.65;

    /// <summary>
    /// Yeni klasör ↔ aynı çekmecedeki mevcut klasör ("Ad: açıklama") benzerliği bunun üstündeyse yenisi açılmaz, mevcut kullanılır.
    /// Ölçüm (2026-10-28): aynı konu 0,59-0,84, farklı 0,38-0,60 ("Frontend"/"Backend" 0,60 ayrı kalmalı).
    /// </summary>
    public double CategoryMergeMinScore { get; set; } = 0.70;

    /// <summary>Bununla <see cref="CategoryMergeMinScore"/> arasında modele "aynı konu mu?" diye sorulur.</summary>
    public double CategoryAskMinScore { get; set; } = 0.55;

    /// <summary>Anahtar kelime aramasından en fazla kaç kayıt eklenir.</summary>
    public int KeywordTopK { get; set; } = 3;

    /// <summary>
    /// Çekirdek profilin bağlamı şişirmemesi için üst sınır (en yeni kayıtlar).
    /// Hangi kategorilerin çekirdek olduğu artık kategori tablosunda (<see cref="MemoryCategory.IsCore"/>), hafıza panelinden değişir.
    /// </summary>
    public int CoreMaxItems { get; set; } = 10;
}
