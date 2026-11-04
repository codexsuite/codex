using System.Net;
using System.Text;

namespace Codex.UnitTest.Contracts;

// A handler that answers from a table keyed by the request URL, and remembers what was asked. No network.
public sealed class StubHttpHandler : HttpMessageHandler, IHttpClientFactory
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);

    public IList<string> Requests { get; } = [];

    public void Answer(string url, Func<HttpResponseMessage> response) => _answers[url] = response;

    public void Json(string url, string json) => Answer(url, () => Reply(HttpStatusCode.OK, json, "application/json"));

    public void Html(string url, string html) => Answer(url, () => Reply(HttpStatusCode.OK, html, "text/html"));

    public void Status(string url, HttpStatusCode status) => Answer(url, () => new HttpResponseMessage(status));

    public void Redirect(string url, string to) => Answer(url, () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri(to);
        return response;
    });

    public static HttpResponseMessage Reply(HttpStatusCode status, string body, string contentType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        return Task.FromResult(_answers.TryGetValue(url, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
