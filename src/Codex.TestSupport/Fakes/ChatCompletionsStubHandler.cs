using System.IO.Pipelines;
using System.Net;
using System.Text;

namespace Codex.TestSupport.Fakes;

// Stands in for the socket behind the named Chat Completions / models HttpClients (ai-openai-compatible, ai-openrouter):
// it replays hand-written responses from Fixtures/ai/openai/ and never touches the network. Every request is kept
// (URL, body, headers) so a test can read what was sent. A stream is replayed frame by frame; `gate` holds it after the
// first text delta and `failWith` breaks it after the last kept one. Reusable by the API lanes.
public sealed class ChatCompletionsStubHandler : HttpMessageHandler, IHttpClientFactory
{
    public const string StreamFixture = "chat-stream.sse";

    private readonly Func<HttpResponseMessage> _respond;

    private ChatCompletionsStubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

    public IList<StubbedRequest> Requests { get; } = [];

    public int RequestCount => Requests.Count;

    public StubbedRequest Last => Requests[^1];

    // Answers the first request from steps[0], the next from steps[1], and so on; the last answer repeats.
    public static ChatCompletionsStubHandler Sequence(params ChatCompletionsStubHandler[] steps)
    {
        var next = -1;
        return new ChatCompletionsStubHandler(() => steps[Math.Min(Interlocked.Increment(ref next), steps.Length - 1)]._respond());
    }

    // The fixture's stream with only the first `chunks.Count` text deltas (which must be the fixture's own, in order).
    public static ChatCompletionsStubHandler Streaming(IReadOnlyList<string> chunks, Task? gate = null, Exception? failWith = null, string fixture = StreamFixture)
    {
        var frames = Frames(fixture);
        var deltaIndexes = frames.Select((f, i) => (f, i)).Where(x => IsTextDelta(x.f)).Select(x => x.i).ToArray();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (!frames[deltaIndexes[i]].Contains($"\"content\":\"{chunks[i]}\"", StringComparison.Ordinal))
            {
                throw new ArgumentException($"The stream fixture has no text delta {i} with '{chunks[i]}'.", nameof(chunks));
            }
        }

        var lastKept = chunks.Count == 0 ? 0 : deltaIndexes[chunks.Count - 1];
        var steps = new List<object>();
        for (var i = 0; i < frames.Count; i++)
        {
            if (failWith == null ? !deltaIndexes.Contains(i) || i <= lastKept : i <= lastKept)
            {
                steps.Add(frames[i]);
            }

            if (i == deltaIndexes[0] && gate != null)
            {
                steps.Add(gate);
            }
        }

        if (failWith != null)
        {
            steps.Add(failWith);
        }

        return new ChatCompletionsStubHandler(() => EventStream(steps));
    }

    // A response whose whole body is the given hand-written SSE file.
    public static ChatCompletionsStubHandler StreamingFile(string fixture) => new(() => EventStream([.. Frames(fixture)]));

    // A stream that sends `first` and then goes silent until the request is cancelled (the idle limit).
    public static ChatCompletionsStubHandler Silent(string first) => new(() => EventStream([first, new TaskCompletionSource().Task]));

    // A body of `bytes` characters of SSE comment lines, for the response size cap.
    public static ChatCompletionsStubHandler Oversized(int bytes) => new(() => EventStream([.. Enumerable.Repeat(": padding\n", bytes / 10 + 1)]));

    // One line of `bytes` characters with no newline ever sent, for the size cap in the middle of a line.
    public static ChatCompletionsStubHandler Unterminated(int bytes) => new(() => EventStream(["data: ", .. Enumerable.Repeat(new string('a', 100), bytes / 100 + 1)]));

    // A body of the given text as an event stream, for odd frames (keep-alives, non-object JSON).
    public static ChatCompletionsStubHandler Text(string body) => new(() => EventStream([body]));

    // Headers arrive with the status, then the body never finishes (until the read is cancelled).
    public static ChatCompletionsStubHandler HangingBody(HttpStatusCode status) => new(() =>
    {
        var response = EventStream([new TaskCompletionSource().Task]);
        response.StatusCode = status;
        return response;
    });

    // A JSON body for a status (401, 429, 500, a models list with 200, ...).
    public static ChatCompletionsStubHandler Json(HttpStatusCode status, string fixture, string? requestId = null) => new(() =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(File.ReadAllText(FixturePath(fixture)), Encoding.UTF8, "application/json") };
        if (requestId != null)
        {
            response.Headers.TryAddWithoutValidation("x-request-id", requestId);
        }

        return response;
    });

    public static ChatCompletionsStubHandler Redirect(string to) => new(() =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location = new Uri(to);
        return response;
    });

    public static ChatCompletionsStubHandler NetworkDown() => new(() => throw new HttpRequestException("connection refused"));

    public HttpClient CreateClient(string name)
    {
        ClientNames.Add(name);
        return new HttpClient(this, disposeHandler: false);
    }

    public IList<string> ClientNames { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new StubbedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, body, headers));
        return _respond();
    }

    private static bool IsTextDelta(string frame) => System.Text.RegularExpressions.Regex.IsMatch(frame, "\"content\":\"[^\"]+\"");

    private static HttpResponseMessage EventStream(List<object> steps)
    {
        // A write completes only once the reader has taken it, so a failure step lands after every earlier frame was read.
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 0));
        _ = PlayAsync(steps, pipe.Writer);
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task PlayAsync(List<object> steps, PipeWriter writer)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case string frame:
                    await writer.WriteAsync(Encoding.UTF8.GetBytes(frame));
                    break;
                case Task gate:
                    await gate;
                    break;
                case Exception failure:
                    await writer.CompleteAsync(failure);
                    return;
            }
        }

        await writer.CompleteAsync();
    }

    private static string FixturePath(string name) => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "ai", "openai", name);

    private static List<string> Frames(string fixture) =>
        File.ReadAllText(FixturePath(fixture)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(f => f + "\n\n").ToList();
}
