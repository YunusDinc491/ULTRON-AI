using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Briefing;
using Ultron.Core.Calendar;
using Ultron.Core.Settings;
using Ultron.Core.Suggestions;
using Ultron.Infrastructure.Briefing;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

public class SuggestionTests
{
    private const string Umbrella = """{"oneri_var":true,"metin":"Patron, yarın 09:00 dersin var ve yağmur yağacak, şemsiyeni bu akşam hazırla.","neden":"ders + hava"}""";

    // 26 Ekim 2026 pazartesi; ManualTimeProvider'da yerel = UTC.
    private static (ToolHarness H, SuggestionService S, RecordingSuggestionNotifier N) At(int day, int hour, int minute = 0)
    {
        var h = new ToolHarness(new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero));
        var n = new RecordingSuggestionNotifier();
        var s = new SuggestionService(h.Settings, h.Briefing, h.Calendar, h.Reminders, new InMemoryMemoryStore(), new CodingTests.InMemoryCodingStore(),
            h.Phraser, n, h.Time, NullLogger<SuggestionService>.Instance);
        return (h, s, n);
    }

    [Fact]
    public async Task Model_oneri_yok_derse_sessiz_kalir()
    {
        var (h, s, n) = At(26, 12);
        Assert.Null(await s.TryDeliverAsync());
        Assert.Equal(1, h.Phraser.SuggestionCalls);
        Assert.Empty(n.Sent);
    }

    [Fact]
    public async Task Oneri_gonderilir_saatte_bir_dusunur_ve_tekrar_etmez()
    {
        var (h, s, n) = At(26, 12);
        h.Phraser.SuggestionJson = Umbrella;
        Assert.NotNull(await s.TryDeliverAsync());
        Assert.Single(n.Sent);

        h.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(await s.TryDeliverAsync()); // saatte bir düşünür
        Assert.Equal(1, h.Phraser.SuggestionCalls);

        h.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(await s.TryDeliverAsync()); // model aynısını yine önerdi: tekrar filtresi
        Assert.Equal(2, h.Phraser.SuggestionCalls);
        Assert.Single(n.Sent);
    }

    [Theory]
    [InlineData(9, 59)]  // 10:00'dan önce
    [InlineData(21, 30)] // 21:30 ve sonrası
    public async Task Saat_araligi_disinda_dusunmez(int hour, int minute)
    {
        var (h, s, _) = At(26, hour, minute);
        Assert.Null(await s.TryDeliverAsync());
        Assert.Equal(0, h.Phraser.SuggestionCalls);
    }

    [Fact]
    public async Task Derste_ve_kapaliyken_dusunmez()
    {
        var (h, s, _) = At(26, 12);
        await h.Calendar.AddAsync(new CalendarEvent { Title = "Termodinamik", Day = DayOfWeek.Monday, Start = new(11, 0), End = new(13, 0) });
        Assert.Equal("derste", await s.WhyNotNowAsync(h.Time.GetLocalNow().DateTime, h.Time.GetUtcNow().UtcDateTime, default));

        var (h2, s2, _) = At(26, 12);
        await h2.Settings.SetAsync(UltronSettings.SuggestionsPerDay, "kapat");
        Assert.Null(await s2.TryDeliverAsync());
        Assert.Equal(0, h2.Phraser.SuggestionCalls);
    }

    [Fact]
    public async Task Gunluk_sinir_ve_bir_saat_araligi()
    {
        var (h, s, n) = At(26, 12);
        await h.Settings.SetAsync(UltronSettings.SuggestionsPerDay, "1");
        h.Phraser.SuggestionJson = Umbrella;
        Assert.NotNull(await s.TryDeliverAsync());

        var local = h.Time.GetLocalNow().DateTime;
        Assert.Equal("son öneriden 1 saat geçmedi", await s.WhyNotNowAsync(local.AddMinutes(30), h.Time.GetUtcNow().UtcDateTime.AddMinutes(30), default));
        Assert.Equal("günlük sınır doldu", await s.WhyNotNowAsync(local.AddHours(3), h.Time.GetUtcNow().UtcDateTime.AddHours(3), default));
        Assert.Single(n.Sent);
    }

    [Fact]
    public async Task Model_hatasi_oneriyi_bozmaz()
    {
        var (h, s, n) = At(26, 12);
        h.Phraser.SuggestionJson = null;
        Assert.Null(await s.TryDeliverAsync());
        Assert.Empty(n.Sent);
    }

    [Fact]
    public void Tekrar_benzerligi()
    {
        List<SuggestionService.SentSuggestion> history = [new(DateTime.UtcNow, "Patron, yarın yağmur yağacak, şemsiyeni hazırla.")];
        Assert.True(SuggestionService.IsRepeat("Patron yarın yağmur yağacak, şemsiyeni unutma.", history));
        Assert.False(SuggestionService.IsRepeat("Patron, cuma günü Termodinamik sınavın var, bu akşam tekrar yapabilirsin.", history));
    }

    [Theory]
    [InlineData("sınırsız", "sinirsiz")]
    [InlineData("kapalı", "0")]
    [InlineData("iki", "2")]
    [InlineData("3", "3")]
    [InlineData("50", null)]
    public void Oneri_sayisi_bicimi(string value, string? expected) => Assert.Equal(expected, UltronSettings.ParseSuggestionCount(value));
}

public class WakeForBriefingTests
{
    [Fact]
    public async Task Acilinca_kurulur_kapatilinca_silinir_saat_degisince_guncellenir()
    {
        var h = new ToolHarness();
        var fake = new FakeWakeScheduler();
        var wake = new WakeForBriefingService(h.Settings, fake, NullLogger<WakeForBriefingService>.Instance);
        wake.Watch();

        await wake.SyncAsync();
        Assert.Null(fake.Task); // varsayılan kapalı
        Assert.Equal(1, fake.Removed);

        await h.Settings.SetAsync(UltronSettings.WakeForBriefing, "açık");
        await WaitAsync(() => fake.Task is not null);
        Assert.Equal(new TimeOnly(7, 28), fake.Task!.Value.At); // brifing 07:30, 2 dk önce
        Assert.Equal(5, fake.Task.Value.Days.Count);

        await h.Settings.SetAsync(UltronSettings.BriefingTime, "8");
        await WaitAsync(() => fake.Task?.At == new TimeOnly(7, 58));

        await h.Settings.SetAsync(UltronSettings.WakeForBriefing, "kapalı");
        await WaitAsync(() => fake.Task is null);
        Assert.StartsWith("kapalı", await wake.DescribeAsync());
    }

    [Fact]
    public async Task Durum_metni_zamanlayici_kapaliysa_uyarir()
    {
        var h = new ToolHarness();
        var fake = new FakeWakeScheduler { AllowedOnAc = false };
        var wake = new WakeForBriefingService(h.Settings, fake, NullLogger<WakeForBriefingService>.Instance);
        await h.Settings.SetAsync(UltronSettings.WakeForBriefing, "acik");
        await wake.SyncAsync();
        Assert.Contains("DİKKAT", await wake.DescribeAsync());
    }

    [Fact]
    public void Gorev_xml_secili_gunlerde_uyandirir()
    {
        var xml = WindowsWakeScheduler.TaskXml(new TimeOnly(7, 28), [DayOfWeek.Friday, DayOfWeek.Monday], @"C:\Ultron\Ultron.exe", new DateTime(2026, 10, 27, 12, 0, 0));
        var doc = System.Xml.Linq.XDocument.Parse(xml); // geçerli XML
        Assert.Contains("<WakeToRun>true</WakeToRun>", xml);
        Assert.Contains("<StartBoundary>2026-10-28T07:28:00</StartBoundary>", xml); // bugünün saati geçti: yarından başlar
        Assert.Contains("<DaysOfWeek><Monday/><Friday/></DaysOfWeek>", xml);
        Assert.Contains("<Arguments>--tray</Arguments>", xml);
        Assert.NotNull(doc.Root);
    }

    [Theory]
    [InlineData("    Geçerli AC Güç Ayarı Dizini: 0x00000001\r\n    Geçerli DC Güç Ayarı Dizini: 0x00000000\r\n", true, false)]
    [InlineData("Current AC Power Setting Index: 0x00000002\nCurrent DC Power Setting Index: 0x00000001\n", false, true)]
    [InlineData("hata", null, null)]
    public void Powercfg_ciktisi(string output, bool? ac, bool? dc) => Assert.Equal((ac, dc), WindowsWakeScheduler.ParseWakeTimers(output));

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeWakeScheduler : IWakeScheduler
    {
        public (TimeOnly At, IReadOnlyCollection<DayOfWeek> Days)? Task { get; private set; }
        public int Removed { get; private set; }
        public bool? AllowedOnAc { get; init; } = true;

        public System.Threading.Tasks.Task EnsureAsync(TimeOnly at, IReadOnlyCollection<DayOfWeek> days, CancellationToken cancellationToken = default)
        {
            Task = (at, days);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task RemoveAsync(CancellationToken cancellationToken = default)
        {
            Task = null;
            Removed++;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<WakeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new WakeStatus(Task is not null, AllowedOnAc, AllowedOnAc));
    }
}

internal sealed class RecordingSuggestionNotifier : ISuggestionNotifier
{
    public List<string> Sent { get; } = [];

    public Task NotifySuggestionAsync(string text, CancellationToken cancellationToken = default)
    {
        Sent.Add(text);
        return Task.CompletedTask;
    }
}
