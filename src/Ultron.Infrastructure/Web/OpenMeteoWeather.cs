using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Ultron.Core.Tools;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Web;

/// <summary>appsettings.json "Weather" bölümü. Varsayılan şehir tasarım kararıyla İstanbul.</summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    public string DefaultCity { get; set; } = "İstanbul";

    public double DefaultLatitude { get; set; } = 41.0082;

    public double DefaultLongitude { get; set; } = 28.9784;

    public string ForecastUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    public string GeocodingUrl { get; set; } = "https://geocoding-api.open-meteo.com/v1/search";
}

/// <summary>
/// Open-Meteo: ücretsiz, anahtarsız hava durumu. Dışarı sadece şehir adı / koordinat gider; her istek
/// "giden sorgular" kaydına yazılır (/durum'da görünür).
/// </summary>
internal sealed class OpenMeteoWeather(HttpClient http, IOptions<WeatherOptions> options, IWebCacheStore log, TimeProvider time) : IWeatherService
{
    public async Task<WeatherReport?> GetAsync(string? city, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var (place, lat, lon) = (o.DefaultCity, o.DefaultLatitude, o.DefaultLongitude);
        if (!string.IsNullOrWhiteSpace(city) && !SameCity(city, o.DefaultCity))
        {
            // Şehir → koordinat bir kez sorulur ve hatırlanır: geocoding servisi bazen 7-15+ sn sürüyor (ölçüm), hava servisi 0,2 sn.
            var key = Core.Text.TurkishText.Normalize(city);
            if (!Places.TryGetValue(key, out var found))
            {
                await log.LogOutgoingAsync($"hava durumu konumu: {city}", time.GetUtcNow().UtcDateTime, cancellationToken);
                var geo = await http.GetFromJsonAsync<GeoResponse>(
                    $"{o.GeocodingUrl}?name={Uri.EscapeDataString(city)}&count=1&language=tr&format=json", cancellationToken);
                if (geo?.Results is not [var hit, ..])
                    return null;
                found = (string.IsNullOrEmpty(hit.Admin1) || hit.Admin1 == hit.Name ? hit.Name : $"{hit.Name} ({hit.Admin1})", hit.Latitude, hit.Longitude);
                Places[key] = found;
            }
            (place, lat, lon) = found;
        }

        await log.LogOutgoingAsync($"hava durumu: {place}", time.GetUtcNow().UtcDateTime, cancellationToken);
        var inv = CultureInfo.InvariantCulture;
        var url = $"{o.ForecastUrl}?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                  "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&timezone=auto&forecast_days=3";
        var f = await http.GetFromJsonAsync<ForecastResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException("Open-Meteo boş cevap döndürdü.");

        var days = f.Daily.Time.Select((d, i) => new WeatherDay(
            DateOnly.ParseExact(d, "yyyy-MM-dd", inv),
            f.Daily.Min[i] ?? 0, f.Daily.Max[i] ?? 0, f.Daily.RainChance[i] ?? 0, f.Daily.Code[i] ?? -1)).ToList();
        return new WeatherReport(place, new WeatherNow(f.Current.Temperature, f.Current.Apparent, f.Current.Code, f.Current.Wind), days);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Place, double Lat, double Lon)> Places = new();

    private static bool SameCity(string a, string b) =>
        string.Equals(Core.Text.TurkishText.Normalize(a), Core.Text.TurkishText.Normalize(b), StringComparison.Ordinal);

    private sealed record GeoResponse(List<GeoHit>? Results);

    private sealed record GeoHit(string Name, double Latitude, double Longitude, string? Admin1);

    private sealed record ForecastResponse(CurrentBlock Current, DailyBlock Daily);

    private sealed record CurrentBlock(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("apparent_temperature")] double Apparent,
        [property: JsonPropertyName("weather_code")] int Code,
        [property: JsonPropertyName("wind_speed_10m")] double Wind);

    private sealed record DailyBlock(
        List<string> Time,
        [property: JsonPropertyName("weather_code")] List<int?> Code,
        [property: JsonPropertyName("temperature_2m_max")] List<double?> Max,
        [property: JsonPropertyName("temperature_2m_min")] List<double?> Min,
        [property: JsonPropertyName("precipitation_probability_max")] List<int?> RainChance);
}
