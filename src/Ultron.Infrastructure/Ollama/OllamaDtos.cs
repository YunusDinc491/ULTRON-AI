using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ultron.Infrastructure.Ollama;

// Ollama'nın /api/chat ve /api/embed uç noktalarının istek ve cevap biçimleri.
// Alan adları JSON'da snake_case (num_ctx, done_reason); OllamaJson.Options bunu otomatik çevirir.

internal sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    bool Stream,
    bool Think,
    OllamaRequestOptions Options,
    JsonNode? Format = null,
    IReadOnlyList<OllamaTool>? Tools = null);

/// <summary>Asistan mesajında araç çağrıları; araç mesajında hangi aracın sonucu olduğu (tool_name).</summary>
internal sealed record OllamaMessage(string Role, string Content, IReadOnlyList<OllamaToolCall>? ToolCalls = null, string? ToolName = null, IReadOnlyList<string>? Images = null);

internal sealed record OllamaTool(string Type, OllamaFunction Function);

internal sealed record OllamaFunction(string Name, string Description, JsonNode? Parameters);

internal sealed record OllamaToolCall(OllamaToolCallFunction Function);

internal sealed record OllamaToolCallFunction(string Name, JsonElement Arguments);

internal sealed record OllamaRequestOptions(int NumCtx, int NumPredict, double Temperature, double? RepeatPenalty = null);

/// <summary>Streaming cevabın her satırı (ve streaming olmayan cevabın tamamı) bu biçimdedir.</summary>
internal sealed record OllamaChatChunk(OllamaMessage? Message, bool Done, string? DoneReason, string? Error);

internal sealed record OllamaEmbedRequest(string Model, IReadOnlyList<string> Input);

internal sealed record OllamaEmbedResponse(float[][] Embeddings);

internal static class OllamaJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
