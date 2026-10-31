using System.Text.Json;
using Ultron.Core.Calendar;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

public class SettingsTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("8", "08:00")]
    [InlineData("8:30", "08:30")]
    [InlineData("07.15", "07:15")]
    [InlineData("25:00", null)]
    [InlineData("sabah", null)]
    public void Saat_bicimi(string value, string? expected) => Assert.Equal(expected, UltronSettings.ParseTime(value));

    [Fact]
    public async Task Varsayilanlar_ve_konusarak_degistirme()
    {
        var h = new ToolHarness();
        Assert.Equal("07:30", await h.Settings.GetAsync(UltronSettings.BriefingTime));

        var tool = new ChangeSettingTool(h.Settings);
        var ok = await tool.ExecuteAsync(Json("""{"ayar":"brifing_saati","deger":"8"}"""));
        var bad = await tool.ExecuteAsync(Json("""{"ayar":"brifing_gunleri","deger":"bazen"}"""));
        var unknown = await tool.ExecuteAsync(Json("""{"ayar":"yetki","deger":"tam"}"""));

        Assert.True(ok.Ok);
        Assert.Contains("08:00", ok.ForModel);
        Assert.False(bad.Ok);
        Assert.False(unknown.Ok); // yetki gibi ayarlar konuşarak değişmez (Faz 12: onay kartı)
        Assert.Equal("08:00", await h.Settings.GetAsync(UltronSettings.BriefingTime));
    }

    [Theory]
    [InlineData(23, 30, true)]
    [InlineData(3, 0, true)]
    [InlineData(7, 0, false)]
    [InlineData(14, 0, false)]
    public async Task Sessiz_saatler_gece_yarisini_asar(int hour, int minute, bool quiet)
    {
        var h = new ToolHarness();
        Assert.Equal(quiet, await h.Settings.IsQuietAsync(new DateTime(2026, 10, 26, hour, minute, 0)));
    }

    [Fact]
    public async Task Sessiz_saatte_hatirlatma_sessiz_isaretlenir()
    {
        var h = new ToolHarness(new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero));
        await h.Reminders.AddAsync("Su iç", new DateTime(2026, 10, 26, 23, 30, 0), ReminderRecurrence.Yok);

        await h.Reminders.FireDueAsync();

        Assert.True(Assert.Single(h.Notifier.Notified).Quiet);
    }

    [Fact]
    public async Task Hava_araci_konusarak_ayarlanan_sehri_kullanir()
    {
        var h = new ToolHarness();
        await h.Settings.SetAsync(UltronSettings.DefaultCity, "Ankara");

        await h.Tools.Single(t => t.Definition.Name == "hava_durumu").ExecuteAsync(Json("{}"));

        Assert.Equal("Ankara", Assert.Single(h.Weather.Requests));
    }
}

public class CalendarTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Ders_eklenir_listelenir_cakisma_uyarilir_silinir()
    {
        var h = new ToolHarness();
        var add = new AddClassTool(h.Calendar, h.Time);

        var first = await add.ExecuteAsync(Json("""{"gun":"pazartesi","baslangic":"9","bitis":"11:00","ders":"Termodinamik II","yer":"B-204"}"""));
        var clash = await add.ExecuteAsync(Json("""{"gun":"Pazartesi","baslangic":"10:00","bitis":"12:00","ders":"Fizik"}"""));
        var invalid = await add.ExecuteAsync(Json("""{"gun":"pazartesi","baslangic":"12:00","bitis":"10:00","ders":"X"}"""));

        Assert.True(first.Ok);
        Assert.Contains("Pazartesi 09:00-11:00 Termodinamik II (B-204)", first.ForModel);
        Assert.Contains("Dikkat", clash.ForModel);
        Assert.False(invalid.Ok);

        var list = await new ListClassesTool(h.Calendar).ExecuteAsync(Json("""{"gun":"pazartesi"}"""));
        Assert.Contains("#1", list.ForModel);
        Assert.True((await new DeleteClassTool(h.Calendar).ExecuteAsync(Json("""{"numara":2}"""))).Ok);
        Assert.Single(h.Calendar.Items);
    }
}

public class BriefingTests
{
    // 26 Ekim 2026 pazartesi. ManualTimeProvider'ın saat dilimi UTC: yerel = UTC.
    private static ToolHarness At(int day, int hour, int minute) => new(new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero));

    [Fact]
    public async Task Saati_gelince_bir_kez_verilir()
    {
        var h = At(26, 7, 20);
        Assert.False(await h.Briefing.TryDeliverAsync()); // 07:30'dan önce

        h.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(await h.Briefing.TryDeliverAsync());
        Assert.False(await h.Briefing.TryDeliverAsync()); // aynı gün ikinci kez yok

        var (text, late) = Assert.Single(h.BriefingNotifier.Delivered);
        Assert.StartsWith("Günaydın Patron", text);
        Assert.False(late);
    }

    [Fact]
    public async Task Kapaliyken_kacirilan_acilista_telafi_edilir_ogleden_sonra_atlanir()
    {
        var morning = At(26, 10, 0);
        Assert.True(await morning.Briefing.TryDeliverAsync());
        Assert.True(Assert.Single(morning.BriefingNotifier.Delivered).Late);

        var afternoon = At(26, 13, 0);
        Assert.False(await afternoon.Briefing.TryDeliverAsync());
    }

    [Fact]
    public async Task Hafta_sonu_ve_kapaliyken_verilmez()
    {
        Assert.False(await At(25, 8, 0).Briefing.TryDeliverAsync()); // pazar

        var h = At(26, 8, 0);
        await h.Settings.SetAsync(UltronSettings.BriefingDays, "kapali");
        Assert.False(await h.Briefing.TryDeliverAsync());
    }

    [Fact]
    public async Task Gunun_verileri_ders_hava_ve_hatirlatmalari_icerir()
    {
        var h = At(26, 7, 30);
        await h.Calendar.AddAsync(new CalendarEvent { Title = "Termodinamik II", Day = DayOfWeek.Monday, Start = new(9, 0), End = new(11, 0) });
        await h.Calendar.AddAsync(new CalendarEvent { Title = "Fizik", Day = DayOfWeek.Tuesday, Start = new(9, 0), End = new(11, 0) });
        await h.Reminders.AddAsync("İlacını iç", new DateTime(2026, 10, 26, 10, 0, 0), ReminderRecurrence.Yok, ReminderPriority.Onemli);
        await h.Reminders.AddAsync("Yarınki iş", new DateTime(2026, 10, 27, 10, 0, 0), ReminderRecurrence.Yok);

        var facts = await h.Briefing.BuildFactsAsync(new DateOnly(2026, 10, 26));

        Assert.Contains("09:00-11:00 Termodinamik II", facts);
        Assert.DoesNotContain("Fizik", facts);
        Assert.Contains("İstanbul", facts);
        Assert.Contains("10:00 İlacını iç (önemli)", facts);
        Assert.DoesNotContain("Yarınki iş", facts);
    }

    [Fact]
    public async Task Model_cevap_veremezse_hazir_sablon()
    {
        var h = At(26, 7, 30);
        h.Phraser.BriefingJson = null;

        await h.Briefing.TryDeliverAsync();

        Assert.StartsWith("Günaydın. Gün:", Assert.Single(h.BriefingNotifier.Delivered).Text);
    }

    [Fact]
    public async Task Hazir_sablon_hitap_ayarliysa_onunla_selamlar()
    {
        var h = At(26, 7, 30);
        h.Phraser.BriefingJson = null;
        await h.Settings.SetAsync(Ultron.Core.Settings.UltronSettings.UserAddress, "Ece");

        await h.Briefing.TryDeliverAsync();

        Assert.StartsWith("Günaydın Ece. Gün:", Assert.Single(h.BriefingNotifier.Delivered).Text);
    }
}

public class CalendarDuplicateTests
{
    [Fact]
    public async Task Ayni_ders_tekrar_eklenmez_bilgileri_guncellenir()
    {
        var h = new ToolHarness();
        var add = new AddClassTool(h.Calendar, h.Time);

        await add.ExecuteAsync(JsonDocument.Parse("""{"gun":"sali","baslangic":"09:00","bitis":"11:50","ders":"Heat Transfer","yer":"E2-21"}""").RootElement);
        var again = await add.ExecuteAsync(JsonDocument.Parse("""{"gun":"sali","baslangic":"09:00","bitis":"11:50","ders":"heat transfer","hoca":"Muammer Kanlı"}""").RootElement);

        var item = Assert.Single(h.Calendar.Items);
        Assert.Contains("zaten programda", again.ForModel);
        Assert.Equal("E2-21", item.Location);
        Assert.Equal("Muammer Kanlı", item.Instructor);
        Assert.Contains("Heat Transfer (E2-21, Muammer Kanlı)", Days.Describe(item));
    }
}

public class DeclineTests
{
    [Fact]
    public async Task Arac_secilmezse_reddedildi_isaretlenir()
    {
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", []));
        chat.ToolTurns.Enqueue(new ToolTurn("", []));

        var result = await new ToolHarness().Runner(chat).RunAsync([new(Ultron.Core.Chat.Role.User, "Notlarımı kağıda alırım")], expectCall: true);

        Assert.True(result.Declined);
    }
}
