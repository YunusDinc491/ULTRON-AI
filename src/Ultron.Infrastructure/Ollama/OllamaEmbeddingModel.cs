using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ultron.Core.Embeddings;

namespace Ultron.Infrastructure.Ollama;

internal sealed class OllamaEmbeddingModel(HttpClient http, IOptions<OllamaOptions> options) : IEmbeddingModel
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var o = options.Value;
        var prefix = purpose == EmbeddingPurpose.Query ? o.EmbeddingQueryPrefix : o.EmbeddingDocumentPrefix;

        using var response = await http.PostAsJsonAsync(
            "api/embed",
            new OllamaEmbedRequest(o.EmbeddingModel, texts.Select(t => prefix + t).ToList()),
            OllamaJson.Options,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OllamaException($"Ollama embed {(int)response.StatusCode} döndürdü: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(OllamaJson.Options, cancellationToken);
        if (result?.Embeddings is not { } embeddings || embeddings.Length != texts.Count)
            throw new OllamaException("Ollama beklenen sayıda vektör döndürmedi.");

        return embeddings;
    }
}
