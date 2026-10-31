namespace Ultron.Core.Memories;

/// <summary>Bir soru için hafızadan getirilenler.</summary>
/// <param name="Core">Çekirdek profil: her zaman göz önünde tutulacak kayıtlar (ör. sağlık).</param>
/// <param name="Relevant">Bu soruyla alakalı bulunan diğer kayıtlar (anlamsal + anahtar kelime).</param>
public sealed record MemoryRecall(IReadOnlyList<Memory> Core, IReadOnlyList<Memory> Relevant)
{
    public static readonly MemoryRecall Empty = new([], []);

    public bool IsEmpty => Core.Count == 0 && Relevant.Count == 0;
}
