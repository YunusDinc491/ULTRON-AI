using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Tools;

namespace Ultron.Infrastructure.Ollama;

internal class OllamaChatModel(HttpClient http, IOptions<OllamaOptions> options) : IChatModel
{
    protected OllamaOptions Settings => options.Value;

    protected virtual string ModelName => Settings.ChatModel;

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        StreamCoreAsync(messages, new OllamaRequestOptions(options.Value.ContextLength, options.Value.MaxResponseTokens, options.Value.Temperature), cancellationToken);

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, double temperature, double repeatPenalty, CancellationToken cancellationToken = default) =>
        StreamCoreAsync(messages, new OllamaRequestOptions(options.Value.ContextLength, options.Value.MaxResponseTokens, temperature, repeatPenalty), cancellationToken);

    private async IAsyncEnumerable<string> StreamCoreAsync(
        IReadOnlyList<ChatMessage> messages,
        OllamaRequestOptions requestOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(messages, stream: true, requestOptions);

        // ResponseHeadersRead: cevabın tamamını beklemeden, satırlar geldikçe okumaya başla.
        using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var chunk = OllamaStreamParser.ParseLine(line);
            if (chunk is null)
                continue;

            if (!string.IsNullOrEmpty(chunk.Message?.Content))
                yield return chunk.Message.Content;

            if (chunk.Done)
                yield break;
        }
    }

    public Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, CancellationToken cancellationToken = default) =>
        CompleteJsonAsync(messages, jsonSchema, options.Value.JsonMaxResponseTokens, cancellationToken);

    public async Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, int maxTokens, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var request = CreateRequest(messages, stream: false, new OllamaRequestOptions(o.ContextLength, maxTokens, o.JsonTemperature))
            with { Format = JsonNode.Parse(jsonSchema) };

        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var chunk = OllamaStreamParser.ParseLine(await response.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new OllamaException("Ollama boş cevap döndürdü.");

        // Sınıra takıldıysa JSON yarım kalmıştır (Faz 1'deki "JSON döngüsü").
        if (chunk.DoneReason == "length")
            throw new OllamaException("JSON cevabı uzunluk sınırına takıldı.");

        return chunk.Message?.Content ?? throw new OllamaException("Ollama cevabında mesaj yok.");
    }

    /// <summary>
    /// Araç turu. Araç sunulduysa karar <see cref="ToolPlanner"/> ile JSON şemasına zorlanarak alınır (ölçüm: yerel tool
    /// calling 4-7/9, JSON kararı 9/9 ve ~0,8 sn). Araç yoksa (aracın sonucundan sonra) düz cevap yazılır.
    /// </summary>
    public async Task<ToolTurn> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (tools.Count > 0)
        {
            var plan = await CompleteJsonAsync(ToolPlanner.BuildMessages(messages, tools), ToolPlanner.Schema(tools), cancellationToken);
            return ToolPlanner.Parse(plan, tools);
        }

        var request = CreateRequest(messages, stream: false, new OllamaRequestOptions(o.ContextLength, o.MaxResponseTokens, o.JsonTemperature));
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var chunk = OllamaStreamParser.ParseLine(await response.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new OllamaException("Ollama boş cevap döndürdü.");
        if (chunk.DoneReason == "length" && chunk.Message?.ToolCalls is not { Count: > 0 })
            throw new OllamaException("Araçlı cevap uzunluk sınırına takıldı.");

        var content = chunk.Message?.Content ?? "";
        IReadOnlyList<ToolCall> calls = chunk.Message?.ToolCalls?.Select(c => new ToolCall(c.Function.Name, c.Function.Arguments.Clone())).ToList() ?? [];
        if (calls.Count == 0)
        {
            // Çağrı düz metin olarak yazıldıysa (gemma4 uzun sistem mesajında böyle yapıyor) metin cevap sayılmaz.
            calls = TextToolCallParser.Parse(content, tools);
            if (calls.Count > 0)
                content = "";
        }
        return new ToolTurn(content, calls);
    }

    private OllamaChatRequest CreateRequest(IReadOnlyList<ChatMessage> messages, bool stream, OllamaRequestOptions requestOptions) =>
        new(
            ModelName,
            messages.Select(ToOllama).ToList(),
            stream,
            Think: false, // düşünme modu kapalı: daha hızlı ve tutarlı (Faz 1 ölçümü)
            requestOptions);

    private static OllamaMessage ToOllama(ChatMessage m) => new(
        ToOllamaRole(m.Role),
        m.Content,
        m.ToolCalls?.Select(c => new OllamaToolCall(new OllamaToolCallFunction(c.Name, c.Arguments))).ToList(),
        m.ToolName,
        m.Images?.Select(Convert.ToBase64String).ToList());

    private async Task<HttpResponseMessage> SendAsync(OllamaChatRequest request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(request, options: OllamaJson.Options),
        };

        var response = await http.SendAsync(httpRequest, completion, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        throw new OllamaException($"Ollama {(int)response.StatusCode} döndürdü: {body}");
    }

    private static string ToOllamaRole(Role role) => role switch
    {
        Role.System => "system",
        Role.User => "user",
        Role.Assistant => "assistant",
        Role.Tool => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}

/// <summary>Aynı Ollama istemcisi, sadece model adı farklı (<see cref="OllamaOptions.CodeModel"/>).</summary>
internal sealed class OllamaCodeChatModel(HttpClient http, IOptions<OllamaOptions> options) : OllamaChatModel(http, options), ICodeChatModel
{
    protected override string ModelName => Settings.CodeModel;

    public string Name => ModelName;
}
