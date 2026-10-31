using System.Net;
using System.Text;

namespace Ultron.Tests.Fakes;

/// <summary>Adrese göre önceden tanımlı cevap veren sahte HTTP katmanı; gelen istekleri kaydeder.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();

    public List<Uri> Requests { get; } = [];

    public StubHttpHandler On(string absoluteUri, string body, string mediaType = "text/html", string? charset = "utf-8", HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[absoluteUri] = () =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new(mediaType) { CharSet = charset };
            return new HttpResponseMessage(status) { Content = content };
        };
        return this;
    }

    public StubHttpHandler Redirect(string from, string to)
    {
        _routes[from] = () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri(to);
            return r;
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var make)
            ? make()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
