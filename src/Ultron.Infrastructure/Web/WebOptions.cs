namespace Ultron.Infrastructure.Web;

/// <summary>appsettings.json içindeki "Web" bölümü.</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    public Uri SearxngUrl { get; set; } = new("http://127.0.0.1:8888");

    /// <summary>Arama sonucundan kaç tanesi alınır.</summary>
    public int MaxResults { get; set; } = 5;

    public string Language { get; set; } = "tr-TR";

    /// <summary>Tek bir sayfanın inmesi için en fazla bekleme. Yavaş bir site cevabı bekletmesin.</summary>
    public int PageTimeoutSeconds { get; set; } = 10;

    /// <summary>İndirilecek en fazla bayt: dev sayfalar ve dosyalar belleği doldurmasın.</summary>
    public int MaxPageBytes { get; set; } = 1_500_000;

    /// <summary>Modele gidecek metnin üst sınırı (sayfa başına). ~4000 karakter ≈ 1000-1500 token.</summary>
    public int MaxPageChars { get; set; } = 4000;

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Ultron/0.1";

    /// <summary>Tavily anahtarı dosyası (tek satır, "tvly-…"): kullanıcı Not Defteri'yle koyar; sohbete yazılmaz.</summary>
    public string TavilyKeyFile { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "tavily-anahtar.txt");

    /// <summary>Dosya yoksa ayar dosyasından (boş bırakılması önerilir).</summary>
    public string? TavilyApiKey { get; set; }

    public int TavilyMaxResults { get; set; } = 6;

    /// <summary>Tavily'nin verdiği sayfa metninin üst sınırı (Medium zaten 20.000 karakter okuyor).</summary>
    public int TavilyMaxContentChars { get; set; } = 20_000;

    /// <summary>PDF'ler (akademik takvim, yönetmelik) HTML'den büyük olur.</summary>
    public int MaxPdfBytes { get; set; } = 15_000_000;

    /// <summary>JavaScript'le dolan sayfalar için görünmez Edge (yoksa bu özellik kapanır).</summary>
    public string? EdgePath { get; set; }
}
