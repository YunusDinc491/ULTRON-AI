using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Sites;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Web;

/// <summary>Faz 24: okul asistanı — takvim PDF'inden tarih, 7/3/1 gün hatırlatmaları, yeni duyuru bildirimi.</summary>
public sealed class Faz24SchoolTests
{
    // Bir üniversitenin 2026-2027 önlisans-lisans takviminin gerçek satırları (PdfPig çıktısı).
    private const string Calendar = """
        GÜZ YARIYILI Başlangıç Tarihi Bitiş Tarihi
        Ders Başlangıç-Bitiş Tarihleri 21 Eylül 2026 Pazartesi 25 Aralık 2026 Cuma
        Ara Sınav Tarihleri 07 Kasım 2026 Cumartesi 15 Kasım 2026 Pazar
        Ara Sınav Notlarının Sisteme Girişi ve İlanı 07 Kasım 2026 Cumartesi 18 Kasım 2026 Çarşamba
        Ara Sınavları İçin Mazeret Sınav Tarihleri (Çift Anadal ve Yandal Dahil ) 24 Kasım 2026 Salı 26 Kasım 2026 Perşembe
        Final Sınav Tarihleri 26 Aralık 2026 Cumartesi 04 Ocak 2027 Pazartesi
        Bütünleme Sınav Tarihleri 11 Ocak 2027 Pazartesi 17 Ocak 2027 Pazar
        """;

    [Fact]
    public void Takvimden_sinav_tarihleri_okunur_not_ve_mazeret_satirlari_atlanir()
    {
        var dates = SchoolAssistant.ExtractDates(Calendar);

        Assert.Equal(
            [("Ara Sınav Tarihleri", new DateOnly(2026, 11, 7)), ("Final Sınav Tarihleri", new DateOnly(2026, 12, 26)), ("Bütünleme Sınav Tarihleri", new DateOnly(2027, 1, 11))],
            dates.Select(d => (d.Label, d.Start)));
    }

    [Fact]
    public async Task Hatirlatmalar_7_3_1_gun_kala_bir_kez_kurulur_baska_programdan_kurulmaz()
    {
        var h = new ToolHarness(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));
        var school = new SchoolAssistant(h.Reminders, h.Settings, h.Time, NullLogger<SchoolAssistant>.Instance);
        const string url = "https://www.ornek.edu.tr/docs/default-source/akademik-takvim/2026---2027/2026-2027-onlisans-lisans.pdf";

        var first = await school.OnPageAsync(url, "2026-2027-onlisans-lisans (PDF)", Calendar);
        var second = await school.OnPageAsync(url, "2026-2027-onlisans-lisans (PDF)", Calendar);
        var other = await school.OnPageAsync(url.Replace("onlisans-lisans", "dis-hekimligi"), "2026-2027-dis-hekimligi (PDF)", Calendar);

        Assert.Equal(9, first); // 3 sınav dönemi × (7, 3, 1 gün)
        Assert.Equal(0, second);
        Assert.Equal(0, other);
        Assert.Contains(h.Store.Items, r => r.Text.Contains("Final Sınav Tarihleri 7 gün sonra") && r.DueAtUtc.Date == new DateTime(2026, 12, 19));
    }

    private sealed class RecordingNotifier : ISiteNotifier
    {
        public List<SitePage> Sent { get; } = [];

        public Task NotifyNewPagesAsync(KnownSite site, IReadOnlyList<SitePage> pages, CancellationToken cancellationToken = default)
        {
            Sent.AddRange(pages);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Sadece_duyuru_sayfalari_bildirilir()
    {
        var h = new ToolHarness();
        var notifier = new RecordingNotifier();
        var school = new SchoolAssistant(h.Reminders, h.Settings, h.Time, NullLogger<SchoolAssistant>.Instance, notifier);
        SitePage P(string url, string title) => new() { Url = url, Title = title, ContentHash = "x" };

        await school.OnNewPagesAsync(new KnownSite { Name = "Okulum", Url = "https://www.ornek.edu.tr" },
        [
            P("https://www.ornek.edu.tr/duyurular/detay/butunleme-programi", "Bütünleme programı yayınlandı"),
            P("https://www.ornek.edu.tr/akademik/fakulteler/muhendislik", "Mühendislik Fakültesi"),
        ]);

        Assert.Equal("Bütünleme programı yayınlandı", Assert.Single(notifier.Sent).Title);
    }
}
