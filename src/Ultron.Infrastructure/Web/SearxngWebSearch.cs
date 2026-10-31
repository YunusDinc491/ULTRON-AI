using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Web;

/// <summary>Yerel SearXNG'nin JSON çıktısını okur (docker/searxng).</summary>
internal sealed class SearxngWebSearch(HttpClient http, IOptions<WebOptions> options)
{
    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var url = $"search?q={Uri.EscapeDataString(query)}&format=json&language={Uri.EscapeDataString(o.Language)}";

        var response = await http.GetFromJsonAsync<SearxngResponse>(url, cancellationToken)
            ?? throw new InvalidOperationException("SearXNG boş cevap döndürdü.");

        return (response.Results ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Url) && !string.IsNullOrWhiteSpace(r.Title))
            .DistinctBy(r => r.Url) // aynı sayfa birden fazla motordan gelebilir
            .Take(o.MaxResults)
            .Select(r => new WebResult(r.Title!.Trim(), r.Url!, r.Content?.Trim() ?? ""))
            .ToList();
    }

    private sealed record SearxngResponse([property: JsonPropertyName("results")] List<SearxngResult>? Results);

    private sealed record SearxngResult(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content);
}
