using Ultron.Core.Settings;

namespace Ultron.Core.Chat;

/// <summary>
/// Cevap paketi (Faz 19; 2026-10-31'den beri iki paket): yazma kutusunun altındaki seçiciden, kalıcı. Benzetme: aynı restoranın
/// iki menüsü — Ultron doyurucu yemek (yerel), Ultron Pro şefin özel tabağı (Claude, Codex ya da Gemini hesabıyla; tarif deftere yazılır).
/// </summary>
public enum AnswerTier
{
    /// <summary>Ultron: akıllı okuma + Türkçe/İngilizce arama + gerekirse 2. tur; kapsamlı, tamamen yerel model.</summary>
    Medium,

    /// <summary>Ultron Pro: soruyu bağlı hesap (Claude, Codex ya da Gemini) araştırır, cevap Ultron'un hafızasına girer.</summary>
    Pro,
}

public static class AnswerTiers
{
    public const string SettingKey = "cevap_paketi";

    public static string Name(AnswerTier tier) => tier == AnswerTier.Pro ? "Ultron Pro" : "Ultron";

    /// <summary>"pro" dışındaki her değer (eski "1.1" ve "medium" dahil) Ultron'dur.</summary>
    public static AnswerTier Parse(string? value) => value?.Trim().ToLowerInvariant() == "pro" ? AnswerTier.Pro : AnswerTier.Medium;

    public static string Key(AnswerTier tier) => tier == AnswerTier.Pro ? "pro" : "ultron";

    public static async Task<AnswerTier> GetAsync(UltronSettings settings, CancellationToken cancellationToken = default) =>
        Parse(await settings.GetInternalAsync(SettingKey, cancellationToken));

    public static Task SetAsync(UltronSettings settings, AnswerTier tier, CancellationToken cancellationToken = default) =>
        settings.SetInternalAsync(SettingKey, Key(tier), cancellationToken);
}
