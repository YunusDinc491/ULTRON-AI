namespace Ultron.Core.Web;

/// <summary>appsettings.json içindeki "WebAnswer" bölümü.</summary>
public sealed class WebAnswerOptions
{
    public const string SectionName = "WebAnswer";

    /// <summary>Arama sonuçlarından kaç sayfa okunup modele verilir. 3 × ~4000 karakter ≈ 3-4 bin token.</summary>
    public int PagesToRead { get; set; } = 3;

    public int SaatTtlHours { get; set; } = 3;

    public int GunTtlHours { get; set; } = 24;

    public int HaftaTtlHours { get; set; } = 24 * 7;

    public TimeSpan? Ttl(WebFreshness freshness) => freshness switch
    {
        WebFreshness.Saat => TimeSpan.FromHours(SaatTtlHours),
        WebFreshness.Gun => TimeSpan.FromHours(GunTtlHours),
        WebFreshness.Hafta => TimeSpan.FromHours(HaftaTtlHours),
        _ => null,
    };
}
