using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Ultron.Core.Embeddings;
using Ultron.Infrastructure.Ollama;

namespace Ultron.Tests.Ollama;

public class OllamaEmbeddingModelTests
{
    [Theory]
    [InlineData(EmbeddingPurpose.Query, "Instruct: Kullanıcının sorusuna cevap veren bilgiyi veya metin parçasını bul\nQuery: Kedimin adı neydi?")]
    [InlineData(EmbeddingPurpose.Document, "Kedimin adı neydi?")]
    public async Task Amaca_gore_on_ek_eklenir(EmbeddingPurpose purpose, string expectedInput)
    {
        var handler = new CapturingHandler("""{"embeddings":[[0.1,0.2,0.3]]}""");
        var model = new OllamaEmbeddingModel(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            Options.Create(new OllamaOptions()));

        var vectors = await model.EmbedAsync(["Kedimin adı neydi?"], purpose);

        Assert.Equal([0.1f, 0.2f, 0.3f], vectors[0]);
        var body = JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal(expectedInput, body["input"]![0]!.GetValue<string>());
        Assert.Equal("/api/embed", handler.LastUri!.AbsolutePath);
    }

    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
