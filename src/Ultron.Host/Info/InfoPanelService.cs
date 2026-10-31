using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ultron.Contracts;
using Ultron.Core.Calendar;
using Ultron.Core.Memories;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Text;
using Ultron.Core.Tools;

namespace Ultron.Host.Info;

/// <summary>
/// Ana ekranın sağ bilgi paneli (2026-10-31, kullanıcı: "kişisel bilgilere göre bilgilendirme, soldaki gibi"). Takvim (bugünün
/// dersleri, hatırlatmaları, yaklaşan sınav), şehrin havası, dolar/euro/gram altın ve Ultron'un kendisi (çalışma süresi, hafıza,
/// "seni tanıma"). Kurlar ve hafıza 5 dakikada bir, hava 10 dakikada bir yenilenir: masaüstü dakikada bir sorsa da internete
/// sık gidilmez. Kurlar anahtarsız: Yahoo Finance'in açık fiyat verisi, olmazsa TCMB'nin günlük kur dosyası.
/// </summary>
public sealed class InfoPanelService(
    HttpClient http,
    UltronSettings settings,
    ICalendarStore calendar,
    IWeatherService weather,
    ReminderService reminders,
    IMemoryStore memories,
    TimeProvider time,
    ILogger<InfoPanelService> logger)
{
    private static readonly CultureInfo Turkish = new("tr-TR");
    private static readonly TimeSpan RatesEvery = TimeSpan.FromMinutes(5), WeatherEvery = TimeSpan.FromMinutes(10), MemoryEvery = TimeSpan.FromMinutes(5);
    private const double GramsPerOunce = 31.1034768;

    // Önbellekler süreç boyunca ortak (servis her istekte yeniden kurulur).
    private static readonly SemaphoreSlim RatesGate = new(1, 1);
    private static (DateTime At, IReadOnlyList<InfoRateDto> Rates, string? Note)? _rates;
    private static (DateTime At, string City, WeatherReport? Report)? _weather;
    private static (DateTime At, int Count, int Categories, DateTime? First)? _memory;

    public async Task<InfoPanelDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow().DateTime;
        var city = await settings.GetAsync(UltronSettings.DefaultCity, cancellationToken);
        var address = await settings.GetAsync(UltronSettings.UserAddress, cancellationToken);
        var (today, exam) = await TodayAsync(now, cancellationToken);
        var report = await WeatherAsync(city, cancellationToken);
        var (rates, note) = await RatesAsync(cancellationToken);
        var (count, categories, first) = await MemoryAsync(cancellationToken);
        var day = report?.Days.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(now));
        return new InfoPanelDto(
            Date: now.ToString("d MMMM yyyy dddd", Turkish),
            Today: today,
            NextExam: exam,
            City: report?.Place ?? city,
            Weather: report is null ? null : $"{report.Now.Temperature:0}°C · {WeatherCodes.Describe(report.Now.Code)}",
            WeatherDetail: day is null ? null : $"{day.Min:0}–{day.Max:0}°C · yağış %{day.RainChance}",
            Rates: rates,
            RatesNote: note,
            UptimeMinutes: (now - Process.GetCurrentProcess().StartTime).TotalMinutes,
            DaysTogether: first is { } f ? (int)(now.Date - f.ToLocalTime().Date).TotalDays + 1 : null,
            Memories: count,
            Categories: categories,
            KnowPercent: KnowPercent(count, categories, !string.IsNullOrWhiteSpace(address)));
    }

    /// <summary>"Seni tanıma": hitap (15) + hafızadaki bilgi sayısı (en çok 55) + farklı başlık sayısı (en çok 30).</summary>
    internal static int KnowPercent(int memoryCount, int categoryCount, bool hasAddress) =>
        Math.Clamp((hasAddress ? 15 : 0) + Math.Min(55, (int)Math.Round(memoryCount * 1.1)) + Math.Min(30, categoryCount * 5), 0, 100);

    /// <summary>Bugünün dersleri ve hatırlatmaları (saat sırasıyla) ve 30 gün içindeki ilk sınav hatırlatması.</summary>
    private async Task<(IReadOnlyList<string> Lines, string? Exam)> TodayAsync(DateTime now, CancellationToken cancellationToken)
    {
        var items = new List<(TimeOnly At, string Text)>();
        foreach (var c in (await calendar.GetAllAsync(cancellationToken)).Where(c => c.Day == now.DayOfWeek))
            items.Add((c.Start, $"{c.Start:HH\\:mm} {c.Title}"));
        var pending = (await reminders.GetPendingAsync(cancellationToken))
            .Select(r => (r, local: reminders.ToLocal(r.DueAtUtc))).ToList();
        foreach (var (r, local) in pending.Where(x => x.local.Date == now.Date))
            items.Add((TimeOnly.FromDateTime(local), $"{local:HH\\:mm} {r.Text}"));
        var exam = pending.Where(x => x.local >= now && x.local <= now.AddDays(30) && TurkishText.Normalize(x.r.Text).Contains("sinav", StringComparison.Ordinal))
            .OrderBy(x => x.local).Select(x => $"{x.r.Text} · {x.local.ToString("d MMMM", Turkish)}").FirstOrDefault();
        return (items.OrderBy(i => i.At).Select(i => i.Text).Take(6).ToList(), exam);
    }

    private async Task<WeatherReport?> WeatherAsync(string city, CancellationToken cancellationToken)
    {
        if (_weather is { } w && w.City == city && DateTime.UtcNow - w.At < WeatherEvery)
            return w.Report;
        WeatherReport? report = null;
        try
        {
            report = await weather.GetAsync(city, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Bilgi paneli: hava alınamadı");
        }
        _weather = (DateTime.UtcNow, city, report);
        return report;
    }

    private async Task<(int Count, int Categories, DateTime? First)> MemoryAsync(CancellationToken cancellationToken)
    {
        if (_memory is { } m && DateTime.UtcNow - m.At < MemoryEvery)
            return (m.Count, m.Categories, m.First);
        var all = await memories.GetActiveAsync(cancellationToken);
        _memory = (DateTime.UtcNow, all.Count, all.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            all.Count == 0 ? null : all.Min(x => x.CreatedAtUtc));
        return (_memory.Value.Count, _memory.Value.Categories, _memory.Value.First);
    }

    private async Task<(IReadOnlyList<InfoRateDto> Rates, string? Note)> RatesAsync(CancellationToken cancellationToken)
    {
        if (_rates is { } cached && DateTime.UtcNow - cached.At < RatesEvery)
            return (cached.Rates, cached.Note);
        await RatesGate.WaitAsync(cancellationToken);
        try
        {
            if (_rates is { } again && DateTime.UtcNow - again.At < RatesEvery)
                return (again.Rates, again.Note);
            var fresh = await LiveRatesAsync(cancellationToken);
            if (fresh is null)
                fresh = await TcmbRatesAsync(cancellationToken);
            // İkisi de olmazsa eski değerler kalır ("son güncelleme" notuyla); 1 dakika sonra yeniden denenir.
            _rates = fresh is { } f ? (DateTime.UtcNow, f.Rates, f.Note)
                : (DateTime.UtcNow - RatesEvery + TimeSpan.FromMinutes(1), _rates?.Rates ?? [], _rates is null ? "kurlar alınamadı (internet?)" : _rates.Value.Note + " · eski");
            return (_rates.Value.Rates, _rates.Value.Note);
        }
        finally
        {
            RatesGate.Release();
        }
    }

    /// <summary>Yahoo Finance açık fiyat verisi: dolar, euro, ons altın → gram altın TL.</summary>
    private async Task<(IReadOnlyList<InfoRateDto> Rates, string Note)?> LiveRatesAsync(CancellationToken cancellationToken)
    {
        var usd = await QuoteAsync("USDTRY=X", cancellationToken);
        var eur = await QuoteAsync("EURTRY=X", cancellationToken);
        var gold = await QuoteAsync("GC=F", cancellationToken);
        if (usd is null || eur is null)
            return null;
        var list = new List<InfoRateDto> { Rate("Dolar", usd.Value), Rate("Euro", eur.Value) };
        if (gold is { } g)
            list.Add(Rate("Gram altın", (g.Price * usd.Value.Price / GramsPerOunce, g.Previous * usd.Value.Previous / GramsPerOunce)));
        return (list, "canlı · " + time.GetLocalNow().ToString("HH:mm", Turkish));
    }

    internal static InfoRateDto Rate(string name, (double Price, double Previous) q) =>
        new(name, Math.Round(q.Price, 2), q.Previous > 0 ? Math.Round((q.Price - q.Previous) / q.Previous * 100, 2) : null);

    private async Task<(double Price, double Previous)?> QuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Ultron/1.0"); // tarayıcı kimliği olmadan 429 döner
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            return ParseQuote(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug(ex, "Bilgi paneli: {Symbol} alınamadı", symbol);
            return null;
        }
    }

    /// <summary>chart.result[0].meta: regularMarketPrice ve chartPreviousClose.</summary>
    internal static (double Price, double Previous)? ParseQuote(string json)
    {
        var meta = JsonDocument.Parse(json).RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");
        return meta.TryGetProperty("regularMarketPrice", out var p) && p.TryGetDouble(out var price) && price > 0
            ? (price, meta.TryGetProperty("chartPreviousClose", out var c) && c.TryGetDouble(out var previous) ? previous : 0)
            : null;
    }

    /// <summary>Yedek: TCMB günlük kur dosyası (resmi, anahtarsız; günde bir güncellenir, değişim yüzdesi yok).</summary>
    private async Task<(IReadOnlyList<InfoRateDto> Rates, string Note)?> TcmbRatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var xml = await http.GetStringAsync("https://www.tcmb.gov.tr/kurlar/today.xml", timeout.Token);
            var rates = ParseTcmb(xml);
            return rates.Count == 0 ? null : (rates, "TCMB · günlük");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug(ex, "Bilgi paneli: TCMB kurları alınamadı");
            return null;
        }
    }

    internal static IReadOnlyList<InfoRateDto> ParseTcmb(string xml)
    {
        var doc = XDocument.Parse(xml);
        var list = new List<InfoRateDto>();
        foreach (var (code, name) in new[] { ("USD", "Dolar"), ("EUR", "Euro") })
        {
            var selling = doc.Root?.Elements("Currency").FirstOrDefault(c => (string?)c.Attribute("Kod") == code)?.Element("ForexSelling")?.Value;
            if (double.TryParse(selling, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
                list.Add(new InfoRateDto(name, Math.Round(value, 2), null));
        }
        return list;
    }
}
