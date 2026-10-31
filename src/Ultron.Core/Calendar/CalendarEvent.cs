using System.Globalization;
using System.Text;
using System.Text.Json;
using Ultron.Core.Text;
using Ultron.Core.Tools;

namespace Ultron.Core.Calendar;

/// <summary>Haftalık ders programındaki bir ders (her hafta aynı gün ve saatte tekrar eder).</summary>
public sealed class CalendarEvent
{
    public long Id { get; set; }

    public required string Title { get; set; }

    public DayOfWeek Day { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }

    public string? Location { get; set; }

    public string? Instructor { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public interface ICalendarStore
{
    Task<CalendarEvent> AddAsync(CalendarEvent item, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarEvent>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task UpdateAsync(CalendarEvent item, CancellationToken cancellationToken = default);
}

public static class Days
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    public static string Name(DayOfWeek day) => Turkish.DateTimeFormat.GetDayName(day);

    public static DayOfWeek? Parse(string? value) => TurkishText.Normalize(value ?? "") switch
    {
        "pazartesi" or "pzt" => DayOfWeek.Monday,
        "sali" => DayOfWeek.Tuesday,
        "carsamba" or "crs" => DayOfWeek.Wednesday,
        "persembe" or "prs" => DayOfWeek.Thursday,
        "cuma" => DayOfWeek.Friday,
        "cumartesi" or "cmt" => DayOfWeek.Saturday,
        "pazar" => DayOfWeek.Sunday,
        _ => null,
    };

    /// <summary>Pazartesi'den başlayan sıra (Türkiye'de hafta pazartesi başlar).</summary>
    public static int Order(DayOfWeek day) => ((int)day + 6) % 7;

    public static string Describe(CalendarEvent e) =>
        $"{Name(e.Day)} {e.Start:HH\\:mm}-{e.End:HH\\:mm} {Details(e)}";

    /// <summary>"Heat Transfer (E2-21, Muammer Kanlı)".</summary>
    public static string Details(CalendarEvent e)
    {
        var extra = new[] { e.Location, e.Instructor }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return e.Title + (extra.Count > 0 ? $" ({string.Join(", ", extra)})" : "");
    }
}

/// <summary>"Pazartesi 9-11 Termodinamik dersim var" → ders programına ekler.</summary>
public sealed class AddClassTool(ICalendarStore store, TimeProvider time) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ders_ekle",
        "Haftalık ders programına bir ders ekler (her hafta tekrar eder).",
        """
        {"type":"object","properties":{
          "gun":{"type":"string","enum":["pazartesi","sali","carsamba","persembe","cuma","cumartesi","pazar"]},
          "baslangic":{"type":"string","description":"SS:DD (ör. 09:00)"},
          "bitis":{"type":"string","description":"SS:DD (ör. 11:00)"},
          "ders":{"type":"string","description":"Dersin adı"},
          "yer":{"type":"string","description":"Derslik/yer (biliniyorsa)"},
          "hoca":{"type":"string","description":"Dersin hocası (biliniyorsa)"}
        },"required":["gun","baslangic","bitis","ders"]}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var day = Days.Parse(ToolArgs.String(arguments, "gun"));
        var start = Settings.UltronSettings.ParseTime(ToolArgs.String(arguments, "baslangic") ?? "");
        var end = Settings.UltronSettings.ParseTime(ToolArgs.String(arguments, "bitis") ?? "");
        var title = ToolArgs.String(arguments, "ders");
        if (day is null || start is null || end is null || title is null)
            return ToolResult.Fail("gün (pazartesi…pazar), başlangıç ve bitiş (SS:DD) ve ders adı gerekli.");
        var s = TimeOnly.ParseExact(start, "HH:mm", CultureInfo.InvariantCulture);
        var e = TimeOnly.ParseExact(end, "HH:mm", CultureInfo.InvariantCulture);
        if (e <= s)
            return ToolResult.Fail("Bitiş saati başlangıçtan sonra olmalı.");

        var location = ToolArgs.String(arguments, "yer");
        var instructor = ToolArgs.String(arguments, "hoca");

        // Aynı ders (aynı gün, saat ve ad) zaten varsa yeni kayıt açılmaz; yeni bilgiyle güncellenir.
        // (Canlı: "tüm dersler ana kampüste" bilgisi aynı dersi ikinci kez eklemişti.)
        var all = await store.GetAllAsync(cancellationToken);
        if (all.FirstOrDefault(x => x.Day == day && x.Start == s && x.End == e && TurkishText.Normalize(x.Title) == TurkishText.Normalize(title)) is { } same)
        {
            same.Location = location ?? same.Location;
            same.Instructor = instructor ?? same.Instructor;
            await store.UpdateAsync(same, cancellationToken);
            return ToolResult.Success($"Bu ders zaten programda (#{same.Id}), bilgileri güncellendi: {Days.Describe(same)}.");
        }

        var item = await store.AddAsync(new CalendarEvent
        {
            Title = title, Day = day.Value, Start = s, End = e,
            Location = location, Instructor = instructor, CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        var clash = (await store.GetAllAsync(cancellationToken))
            .Where(x => x.Id != item.Id && x.Day == item.Day && x.Start < item.End && item.Start < x.End).ToList();
        return ToolResult.Success($"Ders eklendi (#{item.Id}): {Days.Describe(item)}." +
            (clash.Count > 0 ? $" Dikkat: aynı saatte {string.Join(", ", clash.Select(Days.Describe))} da var." : ""));
    }
}

public sealed class ListClassesTool(ICalendarStore store) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ders_programi",
        "Haftalık ders programını numaralarıyla gösterir; gün verilirse sadece o günü.",
        """{"type":"object","properties":{"gun":{"type":"string","enum":["pazartesi","sali","carsamba","persembe","cuma","cumartesi","pazar"]}}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var day = Days.Parse(ToolArgs.String(arguments, "gun"));
        var items = (await store.GetAllAsync(cancellationToken)).Where(x => day is null || x.Day == day)
            .OrderBy(x => Days.Order(x.Day)).ThenBy(x => x.Start).ToList();
        if (items.Count == 0)
            return ToolResult.Success(day is null ? "Ders programı boş." : $"{Days.Name(day.Value)} günü ders yok.");
        var sb = new StringBuilder();
        foreach (var x in items)
            sb.AppendLine($"#{x.Id} · {Days.Describe(x)}");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

public sealed class DeleteClassTool(ICalendarStore store) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ders_sil",
        "Numarası verilen dersi programdan siler. Numarayı bilmiyorsan önce ders_programi aracını kullan.",
        """{"type":"object","properties":{"numara":{"type":"integer"}},"required":["numara"]}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        ToolArgs.Long(arguments, "numara") is not { } id ? ToolResult.Fail("'numara' gerekli.")
        : await store.DeleteAsync(id, cancellationToken) ? ToolResult.Success($"#{id} numaralı ders programdan çıkarıldı.")
        : ToolResult.Fail($"#{id} numaralı bir ders yok.");
}
