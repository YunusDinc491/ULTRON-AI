using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Infrastructure.Ollama;

namespace Ultron.Tests.Ollama;

public class OllamaChatModelTests
{
    private const string Ndjson = """
        {"message":{"role":"assistant","content":"Merhaba"},"done":false}
        {"message":{"role":"assistant","content":" dünya"},"done":false}
        {"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}
        """;

    [Fact]
    public async Task Parcalar_sirayla_akar()
    {
        var (model, _) = CreateModel(Ndjson);

        var parts = new List<string>();
        await foreach (var part in model.StreamAsync([new(Role.User, "Selam")]))
            parts.Add(part);

        Assert.Equal(["Merhaba", " dünya"], parts);
    }

    [Fact]
    public async Task Istek_ayarlardaki_model_ve_sinirlarla_gonderilir()
    {
        var (model, handler) = CreateModel(Ndjson);

        await foreach (var _ in model.StreamAsync([new(Role.System, "talimat"), new(Role.User, "Selam")])) { }

        var body = JsonNode.Parse(handler.LastRequestBody!)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.False(body["think"]!.GetValue<bool>());
        Assert.Equal(16384, body["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal(2048, body["options"]!["num_predict"]!.GetValue<int>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("/api/chat", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Gorsel_mesajda_base64_images_olarak_gider_gorselsiz_mesajda_alan_yok()
    {
        var (model, handler) = CreateModel(Ndjson);

        await foreach (var _ in model.StreamAsync([new(Role.System, "talimat"), new(Role.User, "Bu ne?", Images: [[1, 2, 3]])])) { }

        var messages = JsonNode.Parse(handler.LastRequestBody!)!["messages"]!.AsArray();
        Assert.Null(messages[0]!["images"]);
        Assert.Equal("AQID", messages[1]!["images"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Basarisiz_durum_kodu_istisna_firlatir()
    {
        var (model, _) = CreateModel("""{"error":"model 'x' not found"}""", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<OllamaException>(async () =>
        {
            await foreach (var _ in model.StreamAsync([new(Role.User, "Selam")])) { }
        });
    }

    private static (OllamaChatModel Model, FakeHandler Handler) CreateModel(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var options = Options.Create(new OllamaOptions { ChatModel = "test-model" });
        return (new OllamaChatModel(http, options), handler);
    }

    /// <summary>Gerçek Ollama yerine sabit bir cevap döndüren sahte HTTP katmanı.</summary>
    private sealed class FakeHandler(string responseBody, HttpStatusCode status) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/x-ndjson") };
        }
    }
}
