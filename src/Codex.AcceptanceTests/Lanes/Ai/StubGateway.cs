using System.Net;
using System.Text;

namespace Codex.AcceptanceTests.Lanes.Ai;

// Stands in for an OpenAI-compatible gateway behind its named HttpClient: GET /models lists two models and a streamed
// POST /chat/completions answers the way Chat Completions does (content deltas, a usage chunk, [DONE]).
public sealed class StubGateway : HttpMessageHandler
{
    public const string FirstModel = "gpt-test";

    public const string Text = "The gateway wrote this sentence.";

    private readonly List<string> _chatBodies = [];

    // The request bodies of the chat calls, to check which model was asked for.
    public IReadOnlyList<string> ChatBodies => _chatBodies;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/models", StringComparison.Ordinal))
        {
            return Json("""{"object":"list","data":[{"id":"gpt-test"},{"id":"gpt-test-mini"}]}""", "application/json");
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/chat/completions", StringComparison.Ordinal))
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _chatBodies.Add(body);
            var sse = new StringBuilder();
            foreach (var word in Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((w, i) => (i == 0 ? "" : " ") + w))
            {
                sse.Append("data: {\"choices\":[{\"delta\":{\"content\":").Append(System.Text.Json.JsonSerializer.Serialize(word)).Append("}}]}\n\n");
            }

            sse.Append("data: {\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":6}}\n\n");
            sse.Append("data: [DONE]\n\n");
            return Json(sse.ToString(), "text/event-stream");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body, string contentType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType),
    };
}
