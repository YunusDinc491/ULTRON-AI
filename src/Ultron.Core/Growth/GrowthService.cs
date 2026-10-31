using System.Globalization;

namespace Ultron.Core.Growth;

/// <summary>Bir gece denetiminin raporu (yazılım cevapları, bileşenler, ders cevapları): Gelişim'de geçmişiyle görünür.</summary>
public sealed class NightlyReport
{
    public long Id { get; init; }

    /// <summary>"yazilim", "bilesen" ya da "ders".</summary>
    public required string Area { get; init; }

    public required string Summary { get; init; }

    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Puanlanan bir quiz (Ders): Gelişim'de puan gidişatı.</summary>
public sealed class QuizResult
{
    public long Id { get; init; }

    public long SessionId { get; init; }

    public int Correct { get; init; }

    public int Total { get; init; }

    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Gelişim için ham satırlar (bir kerede, verilen tarihten beri).</summary>
public sealed record GrowthRaw(
    IReadOnlyList<(DateTime AtUtc, bool FromUser)> Messages,
    IReadOnlyList<(DateTime AtUtc, bool ByClaude, int Rating)> Components,
    IReadOnlyList<(DateTime AtUtc, string Author, bool Unsure)> LessonAnswers,
    IReadOnlyList<QuizResult> Quizzes,
    IReadOnlyList<(DateTime AtUtc, bool Learned)> AnswerReviews,
    IReadOnlyList<NightlyReport> Reports,
    IReadOnlyList<(string Session, string Topic, int Misses, int Hits)> WeakTopics,
    int Recipes,
    int LearnedRecipes);

public interface IGrowthStore
{
    Task AddReportAsync(NightlyReport report, CancellationToken cancellationToken = default);

    Task AddQuizResultAsync(QuizResult result, CancellationToken cancellationToken = default);

    Task<GrowthRaw> LoadAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
}

/// <summary>Bir sayı kartı: başlık, değer ("%42", "3,1 sn"), alt not ve geçen haftaya göre yön (+1 iyi, -1 kötü, 0 yok).</summary>
public sealed record GrowthCard(string Group, string Title, string Value, string Note, int Trend);

/// <summary>Haftalık seri: her hafta için değer (veri yoksa null).</summary>
public sealed record GrowthSeries(string Group, string Title, string Unit, IReadOnlyList<double?> Values, bool HigherIsBetter);

public sealed record GrowthOverview(IReadOnlyList<string> Weeks, IReadOnlyList<GrowthCard> Cards, IReadOnlyList<GrowthSeries> Series,
    IReadOnlyList<NightlyReport> Reports, IReadOnlyList<(string Session, string Topic, int Misses, int Hits)> WeakTopics);

/// <summary>
/// Gelişim panosu (Faz 18, kullanıcının kararları): üstte sayı kartları, altta son 8 haftanın serileri; gece raporlarının geçmişi;
/// Ders'in zayıf konuları. Benzetme: karne — her hafta notlar yazılır, gidişat çizgiden görülür (Claude'a gitme oranı
/// düşüyorsa Ultron öğreniyor demektir).
/// </summary>
public sealed class GrowthService(IGrowthStore store, TimeProvider time)
{
    public const int WeekCount = 8;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public Task RecordReportAsync(string area, string summary, CancellationToken cancellationToken = default) =>
        store.AddReportAsync(new NightlyReport { Area = area, Summary = summary, AtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);

    public async Task<GrowthOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var localToday = time.GetLocalNow().Date;
        var thisMonday = localToday.AddDays(-(((int)localToday.DayOfWeek + 6) % 7));
        var starts = Enumerable.Range(0, WeekCount).Select(i => thisMonday.AddDays(-7 * (WeekCount - 1 - i))).ToList();
        var since = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(starts[0], DateTimeKind.Unspecified), time.LocalTimeZone);
        var raw = await store.LoadAsync(since, cancellationToken);
        return Build(raw, starts, time.LocalTimeZone);
    }

    /// <summary>Ham satırlar → kartlar ve seriler (saf hesap, test edilir).</summary>
    internal static GrowthOverview Build(GrowthRaw raw, IReadOnlyList<DateTime> weekStarts, TimeZoneInfo zone)
    {
        int Week(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            for (var i = weekStarts.Count - 1; i >= 0; i--)
                if (local >= weekStarts[i])
                    return i;
            return -1;
        }

        double?[] Per<T>(IEnumerable<T> rows, Func<T, DateTime> at, Func<List<T>, double?> value)
        {
            var buckets = Enumerable.Range(0, weekStarts.Count).Select(_ => new List<T>()).ToArray();
            foreach (var r in rows)
                if (Week(at(r)) is var w and >= 0)
                    buckets[w].Add(r);
            return buckets.Select(b => b.Count == 0 ? null : value(b)).ToArray();
        }

        // Ana: mesaj sayısı ve cevap süresi (kullanıcı mesajından sonraki ilk asistan mesajına kadar, 2 dk üstü sayılmaz).
        var replies = new List<(DateTime At, double Seconds)>();
        var ordered = raw.Messages.OrderBy(m => m.AtUtc).ToList();
        for (var i = 0; i < ordered.Count - 1; i++)
            if (ordered[i].FromUser && !ordered[i + 1].FromUser && (ordered[i + 1].AtUtc - ordered[i].AtUtc).TotalSeconds is var s and >= 0 and < 120)
                replies.Add((ordered[i].AtUtc, s));
        var messages = Per(raw.Messages.Where(m => m.FromUser), m => m.AtUtc, b => b.Count);
        var latency = Per(replies, r => r.At, b => Median(b.Select(x => x.Seconds)));

        var claude = Per(raw.Components, c => c.AtUtc, b => 100.0 * b.Count(c => c.ByClaude) / b.Count);
        var thumbs = Per(raw.Components.Where(c => c.Rating != 0), c => c.AtUtc, b => 100.0 * b.Count(c => c.Rating > 0) / b.Count);
        var quiz = Per(raw.Quizzes.Where(q => q.Total > 0), q => q.AtUtc, b => 100.0 * b.Sum(q => q.Correct) / b.Sum(q => q.Total));
        var calc = Per(raw.LessonAnswers.Where(a => a.Author == "hesap"), a => a.AtUtc, b => 100.0 * b.Count(a => !a.Unsure) / b.Count);
        var learned = Per(raw.AnswerReviews, r => r.AtUtc, b => b.Count(r => r.Learned));

        var series = new List<GrowthSeries>
        {
            new("Genel", "Mesaj (haftalık)", "", messages, true),
            new("Genel", "Cevap süresi (medyan)", "sn", latency, false),
            new("İş", "Claude'a giden bileşen", "%", claude, false),
            new("İş", "👍 oranı", "%", thumbs, true),
            new("Ders", "Quiz başarısı", "%", quiz, true),
            new("Ders", "Hesap doğruluğu", "%", calc, true),
            new("Öğrenme", "Öğrenilen düzeltme", "", learned, true),
        };

        var cards = new List<GrowthCard>
        {
            Card("Genel", "Bu hafta mesaj", messages, "", true, "konuşma"),
            Card("Genel", "Cevap süresi", latency, " sn", false, "medyan, bu hafta"),
            Card("İş", "Claude'a giden", claude, "%", false, $"{raw.Components.Count} bileşen sürümü"),
            Card("İş", "👍 oranı", thumbs, "%", true, $"tarif defteri {raw.Recipes} ({raw.LearnedRecipes} öğrenilen)"),
            Card("Ders", "Quiz başarısı", quiz, "%", true, $"{raw.Quizzes.Count} quiz"),
            Card("Ders", "Hesap doğruluğu", calc, "%", true, $"{raw.LessonAnswers.Count(a => a.Author == "claude")} soru Claude'a gitti"),
            Card("Öğrenme", "Öğrenilen düzeltme", learned, "", true, $"toplam {raw.AnswerReviews.Count(r => r.Learned)}"),
        };

        var weeks = weekStarts.Select(w => w.ToString("d MMM", Tr)).ToList();
        return new GrowthOverview(weeks, cards, series, raw.Reports.OrderByDescending(r => r.AtUtc).Take(40).ToList(),
            raw.WeakTopics.Where(t => t.Misses > t.Hits).OrderByDescending(t => t.Misses - t.Hits).Take(10).ToList());
    }

    /// <summary>Son haftanın değeri; yoksa son dolu hafta. Yön: son iki dolu haftanın farkı (iyi yöne +1).</summary>
    private static GrowthCard Card(string group, string title, double?[] values, string unit, bool higherIsBetter, string note)
    {
        var filled = values.Select((v, i) => (v, i)).Where(x => x.v is not null).ToList();
        if (filled.Count == 0)
            return new GrowthCard(group, title, "–", note, 0);
        var last = filled[^1];
        var trend = filled.Count >= 2 && Math.Abs(last.v!.Value - filled[^2].v!.Value) > 0.5
            ? Math.Sign(last.v.Value - filled[^2].v!.Value) * (higherIsBetter ? 1 : -1)
            : 0;
        var value = unit == "%" ? "%" + Math.Round(last.v!.Value).ToString(Tr) : Format(last.v!.Value) + unit;
        return new GrowthCard(group, title, value, last.i == values.Length - 1 ? note : note + " · son veri " + (values.Length - 1 - last.i) + " hafta önce", trend);
    }

    private static string Format(double v) => v >= 10 || v == Math.Floor(v) ? Math.Round(v).ToString(Tr) : v.ToString("0.0", Tr);

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
