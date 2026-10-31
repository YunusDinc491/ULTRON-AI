using Ultron.Core.Growth;

namespace Ultron.Tests.Growth;

public sealed class GrowthServiceTests
{
    private static readonly IReadOnlyList<DateTime> Weeks = Enumerable.Range(0, 8).Select(i => new DateTime(2026, 9, 7).AddDays(7 * i)).ToList(); // son: 26 Ekim

    private static GrowthRaw Raw(
        IReadOnlyList<(DateTime, bool)>? messages = null, IReadOnlyList<(DateTime, bool, int)>? components = null,
        IReadOnlyList<QuizResult>? quizzes = null, IReadOnlyList<(DateTime, string, bool)>? lesson = null) =>
        new(messages ?? [], components ?? [], lesson ?? [], quizzes ?? [], [], [new NightlyReport { Area = "ders", Summary = "2 cevap incelendi" }],
            [("Termo", "Entropi", 3, 1), ("Termo", "Carnot", 1, 2)], 47, 2);

    [Fact]
    public void Haftalara_bolunur_cevap_suresi_medyan_ve_iki_dakikadan_uzun_sayilmaz()
    {
        var t = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
        var o = GrowthService.Build(Raw(messages:
        [
            (t, true), (t.AddSeconds(4), false),
            (t.AddMinutes(10), true), (t.AddMinutes(10).AddSeconds(8), false),
            (t.AddMinutes(20), true), (t.AddMinutes(25), false), // 5 dk: sayılmaz
            (t.AddDays(-7), true), (t.AddDays(-7).AddSeconds(10), false),
        ]), Weeks, TimeZoneInfo.Utc);

        var messages = o.Series.Single(s => s.Title == "Mesaj (haftalık)" && s.Group == "Genel").Values;
        Assert.Equal(3, messages[^1]);
        Assert.Equal(1, messages[^2]);
        Assert.Null(messages[0]);
        Assert.Equal(6, o.Series.Single(s => s.Title.StartsWith("Cevap süresi")).Values[^1]); // (4+8)/2
        var card = o.Cards.Single(c => c.Title == "Cevap süresi");
        Assert.Equal("6 sn", card.Value);
        Assert.Equal(1, card.Trend); // 10 sn → 6 sn: iyi yönde
    }

    [Fact]
    public void Claude_orani_thumbs_quiz_ve_hesap_dogrulugu()
    {
        var t = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
        var o = GrowthService.Build(Raw(
            components: [(t, true, 1), (t, false, 1), (t, false, -1), (t, false, 0)],
            quizzes: [new QuizResult { Correct = 3, Total = 4, AtUtc = t }, new QuizResult { Correct = 1, Total = 4, AtUtc = t }],
            lesson: [(t, "hesap", false), (t, "hesap", false), (t, "hesap", true), (t, "claude", false)]), Weeks, TimeZoneInfo.Utc);

        Assert.Equal("%25", o.Cards.Single(c => c.Title == "Claude'a giden").Value);
        Assert.Equal("%67", o.Cards.Single(c => c.Title == "👍 oranı").Value);
        Assert.Equal("%50", o.Cards.Single(c => c.Title == "Quiz başarısı").Value);
        Assert.Equal("%67", o.Cards.Single(c => c.Title == "Hesap doğruluğu").Value);
        Assert.Contains("1 soru Claude'a gitti", o.Cards.Single(c => c.Title == "Hesap doğruluğu").Note);
        Assert.Equal(["Entropi"], o.WeakTopics.Select(w => w.Topic));
        Assert.Single(o.Reports);
    }

    [Fact]
    public void Veri_yoksa_kart_tire_gosterir() =>
        Assert.All(GrowthService.Build(Raw(), Weeks, TimeZoneInfo.Utc).Cards.Where(c => c.Group != "Öğrenme"), c => Assert.Equal("–", c.Value));
}
