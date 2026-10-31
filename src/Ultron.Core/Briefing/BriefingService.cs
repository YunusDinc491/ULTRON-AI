using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ultron.Core.Calendar;
using Ultron.Core.Chat;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;

namespace Ultron.Core.Briefing;

/// <summary>Sabah brifingini kullanıcıya iletir (Host'ta: masaüstüne SignalR olayı).</summary>
public interface IBriefingNotifier
{
    /// <param name="late">Ultron kapalıydı, brifing açılışta telafi ediliyor.</param>
    Task NotifyBriefingAsync(string text, bool late, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sabah brifingi: bugünün dersleri + hava + bugünün hatırlatmaları → kısa, konuşur gibi birkaç cümle.
/// Saati gelince kendiliğinden (bugün verilmediyse), Ultron kapalıyken kaçırıldıysa açılışta öğlene kadar telafi.
/// </summary>
public sealed class BriefingService(
    UltronSettings settings,
    ICalendarStore calendar,
    IWeatherService weather,
    ReminderService reminders,
    IChatModel chat,
    IBriefingNotifier notifier,
    TimeProvider time,
    ILogger<BriefingService> logger)
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Brifing öğleden sonra artık "sabah brifingi" değil: o gün atlanır.</summary>
    public static readonly TimeOnly LatestDelivery = new(12, 0);

    /// <summary>"Günaydın" ya da hitap ayarlıysa "Günaydın Ece" (Faz 26).</summary>
    internal static string Greeting(string? address) => string.IsNullOrWhiteSpace(address) ? "Günaydın" : $"Günaydın {address.Trim()}";

    private static string Prompt(string? address) => $"""
        Kullanıcıya sabah brifingi veriyorsun. Aşağıdaki VERİLERE dayanarak 3-5 kısa, konuşur gibi Türkçe cümle yaz.
        "{Greeting(address)}" diye başla. Sırayla: bugünün dersleri (saat ve ders adı), hava (yağış ihtimali %50 ve üstüyse şemsiye,
        soğuksa mont uyarısı), bugünün hatırlatmaları. Veri yoksa o konuyu kısaca geç ("Bugün dersin yok"), ASLA uydurma.
        Madde işareti, başlık ve emoji kullanma; sesli okunacak. Saatleri ve sayıları RAKAMLA yaz (09:00, 13:00-15:00, %80, 17 derece);
        sayıları yazıyla yazma ("dokuzdan", "yüz yüzde" gibi).
        """;

    private const string Schema = """{"type":"object","properties":{"metin":{"type":"string"}},"required":["metin"]}""";

    /// <summary>Bir günün ham verileri (brifing ve "bugün neler var?" aracı için).</summary>
    public async Task<string> BuildFactsAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"Gün: {day.ToString("d MMMM yyyy dddd", Turkish)}.\n");

        var classes = (await calendar.GetAllAsync(cancellationToken)).Where(c => c.Day == day.DayOfWeek).OrderBy(c => c.Start).ToList();
        sb.AppendLine(classes.Count == 0
            ? "Dersler: bugün ders yok."
            : "Dersler: " + string.Join("; ", classes.Select(c => $"{c.Start:HH\\:mm}-{c.End:HH\\:mm} {Days.Details(c)}")) + ".");

        var city = await settings.GetAsync(UltronSettings.DefaultCity, cancellationToken);
        try
        {
            if (await weather.GetAsync(city, cancellationToken) is { } w)
            {
                var d = w.Days.FirstOrDefault(x => x.Date == day);
                sb.AppendLine($"Hava ({w.Place}): şu an {w.Now.Temperature:0}°C, {WeatherCodes.Describe(w.Now.Code)}" +
                    (d is null ? "." : $"; gün içinde {d.Min:0}-{d.Max:0}°C, {WeatherCodes.Describe(d.Code)}, yağış ihtimali %{d.RainChance}."));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Brifing için hava alınamadı");
            sb.AppendLine("Hava: alınamadı (internet yok olabilir).");
        }

        var todays = (await reminders.GetPendingAsync(cancellationToken))
            .Select(r => (r, local: reminders.ToLocal(r.DueAtUtc)))
            .Where(x => DateOnly.FromDateTime(x.local) == day).OrderBy(x => x.local).ToList();
        sb.AppendLine(todays.Count == 0
            ? "Hatırlatmalar: bugün yok."
            : "Hatırlatmalar: " + string.Join("; ", todays.Select(x => $"{x.local:HH\\:mm} {x.r.Text} ({ReminderSchedule.Describe(x.r.Priority)})")) + ".");
        return sb.ToString().TrimEnd();
    }

    public async Task<string> ComposeAsync(DateOnly day, bool late, CancellationToken cancellationToken = default)
    {
        var facts = await BuildFactsAsync(day, cancellationToken);
        var address = await settings.GetAsync(UltronSettings.UserAddress, cancellationToken);
        try
        {
            var json = await chat.CompleteJsonAsync(
                [new ChatMessage(Role.System, Prompt(address) + (late ?"\nBrifing geç veriliyor (bilgisayar kapalıydı); bunu kısaca belirt." : "")),
                 new ChatMessage(Role.User, "VERİLER:\n" + facts)],
                Schema, cancellationToken);
            if (JsonDocument.Parse(json).RootElement.GetProperty("metin").GetString()?.Trim() is { Length: >= 20 } text)
                return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) // HTTP zaman aşımı da "iptal" türünde gelir: hata sayılır
        {
            logger.LogWarning(ex, "Brifing metni üretilemedi, hazır şablon kullanılıyor");
        }
        return Greeting(address) + ". " + facts.Replace("\n", " ");
    }

    /// <summary>Zamanlayıcı her bakışta çağırır: brifing günü, saati gelmiş, bugün verilmemiş ve öğle olmamışsa verir.</summary>
    public async Task<bool> TryDeliverAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(now);
        var at = await settings.GetTimeAsync(UltronSettings.BriefingTime, cancellationToken);
        var clock = TimeOnly.FromDateTime(now);
        if (clock < at || clock >= LatestDelivery || !await settings.IsBriefingDayAsync(now, cancellationToken))
            return false;
        if (await settings.GetInternalAsync(UltronSettings.LastBriefingDate, cancellationToken) == today.ToString("yyyy-MM-dd"))
            return false;

        // Önce "verildi" diye işaretle: metin üretilirken bir sonraki bakış ikinci kez başlatmasın.
        await settings.SetInternalAsync(UltronSettings.LastBriefingDate, today.ToString("yyyy-MM-dd"), cancellationToken);
        var late = clock - at > TimeSpan.FromMinutes(15);
        var text = await ComposeAsync(today, late, cancellationToken);
        await notifier.NotifyBriefingAsync(text, late, cancellationToken);
        logger.LogInformation("Sabah brifingi verildi{Late}: {Text}", late ? " (telafi)" : "", text);
        return true;
    }
}

/// <summary>"Bugün neler var?", "yarın ne derslerim var?" → günün dersleri, havası ve hatırlatmaları.</summary>
public sealed class DaySummaryTool(BriefingService briefing, TimeProvider time) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "gunluk_ozet",
        "Bugünün ya da yarının özetini verir: dersler, hava ve hatırlatmalar. 'Bugün neler var', 'yarın derslerim ne', 'brifing ver' gibi isteklerde kullan.",
        """{"type":"object","properties":{"gun":{"type":"string","enum":["bugun","yarin"]}}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var day = ToolArgs.String(arguments, "gun") == "yarin" ? today.AddDays(1) : today;
        return ToolResult.Success(await briefing.BuildFactsAsync(day, cancellationToken));
    }
}
