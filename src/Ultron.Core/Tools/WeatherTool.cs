using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ultron.Core.Tools;

public sealed record WeatherNow(double Temperature, double ApparentTemperature, int Code, double WindKmh);

public sealed record WeatherDay(DateOnly Date, double Min, double Max, int RainChance, int Code);

public sealed record WeatherReport(string Place, WeatherNow Now, IReadOnlyList<WeatherDay> Days);

/// <summary>Hava durumu kaynağı (Infrastructure'da Open-Meteo). Şehir boşsa varsayılan şehir (İstanbul).</summary>
public interface IWeatherService
{
    /// <returns>Şehir bulunamazsa null.</returns>
    Task<WeatherReport?> GetAsync(string? city, CancellationToken cancellationToken = default);
}

/// <summary>Hava durumu aracı: anlık durum + 3 günlük tahmin. Web aramasından hızlı ve kesin.</summary>
public sealed class WeatherTool(IWeatherService weather, Settings.UltronSettings? settings = null) : ITool
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    public ToolDefinition Definition { get; } = new(
        "hava_durumu",
        "Bir şehrin anlık hava durumunu ve önümüzdeki 3 günün tahminini verir. Şehir söylenmediyse boş bırak (kullanıcının varsayılan şehri).",
        """{"type":"object","properties":{"sehir":{"type":"string","description":"Şehir adı (ör. Ankara). Söylenmediyse boş bırak."}}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // Şehir söylenmediyse kullanıcının konuşarak ayarladığı varsayılan şehir.
        var city = ToolArgs.String(arguments, "sehir")
            ?? (settings is null ? null : await settings.GetAsync(Settings.UltronSettings.DefaultCity, cancellationToken));
        var report = await weather.GetAsync(city, cancellationToken);
        if (report is null)
            return ToolResult.Fail($"'{city}' adında bir yer bulunamadı.");

        var sb = new StringBuilder($"{report.Place} için hava durumu (Open-Meteo):\n");
        sb.AppendLine($"Şu an: {report.Now.Temperature:0}°C (hissedilen {report.Now.ApparentTemperature:0}°C), {WeatherCodes.Describe(report.Now.Code)}, rüzgar {report.Now.WindKmh:0} km/sa.");
        foreach (var d in report.Days)
            sb.AppendLine($"{d.Date.ToString("d MMMM dddd", Turkish)}: {d.Min:0}°C / {d.Max:0}°C, {WeatherCodes.Describe(d.Code)}, yağış ihtimali %{d.RainChance}.");
        return ToolResult.Success(sb.ToString().TrimEnd());
    }
}

/// <summary>WMO hava kodları → Türkçe.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "açık",
        1 => "çoğunlukla açık",
        2 => "parçalı bulutlu",
        3 => "kapalı",
        45 or 48 => "sisli",
        51 or 53 or 55 => "çiseleyen yağmur",
        56 or 57 => "dondurucu çisenti",
        61 => "hafif yağmurlu",
        63 => "yağmurlu",
        65 => "kuvvetli yağmurlu",
        66 or 67 => "dondurucu yağmur",
        71 => "hafif kar yağışlı",
        73 => "kar yağışlı",
        75 => "yoğun kar yağışlı",
        77 => "kar taneli",
        80 or 81 => "sağanak yağışlı",
        82 => "şiddetli sağanak",
        85 or 86 => "kar sağanaklı",
        95 => "gök gürültülü fırtına",
        96 or 99 => "dolu ile fırtına",
        _ => "bilinmeyen durum",
    };
}
