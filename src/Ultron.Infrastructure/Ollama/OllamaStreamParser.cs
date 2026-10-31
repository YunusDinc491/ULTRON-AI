using System.Text.Json;

namespace Ultron.Infrastructure.Ollama;

/// <summary>
/// Ollama streaming cevabı NDJSON'dur: her satır ayrı bir JSON nesnesi.
/// Örnek: {"message":{"role":"assistant","content":"Mer"},"done":false}
/// </summary>
internal static class OllamaStreamParser
{
    /// <returns>Boş satırda null; Ollama hata satırı gönderirse <see cref="OllamaException"/>.</returns>
    public static OllamaChatChunk? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, OllamaJson.Options)
            ?? throw new OllamaException("Ollama ayrıştırılamayan bir satır gönderdi.");

        if (chunk.Error is not null)
            throw new OllamaException(chunk.Error);

        return chunk;
    }
}
