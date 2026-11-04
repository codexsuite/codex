using System.IO.Pipelines;
using System.Net;
using System.Text;

namespace Codex.TestSupport.Fakes;

// Stands in for the socket behind the named "anthropic" HttpClient: it replays a hand-written Messages API response
// (Fixtures/ai/*, following the documented event format) and never touches the network. A stream is replayed frame by
// frame from anthropic-stream.sse, so a test can hold it after the first text delta (`gate`) or break it after the last
// one (`failWith`).
public sealed class AnthropicStubHandler : HttpMessageHandler, IHttpClientFactory
{
    public const string StreamFixture = "anthropic-stream.sse";

    private readonly Func<HttpResponseMessage> _respond;
    private int _requests;

    private AnthropicStubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

    // How many requests the client sent (the SDK retries a 429/5xx before the body starts).
    public int RequestCount => _requests;

    public string? RequestBody { get; private set; }

    public string? ApiKeyHeader { get; private set; }

    public string? RequestUrl { get; private set; }

    // Answers the first request from steps[0], the next from steps[1], and so on; the last answer repeats.
    public static AnthropicStubHandler Sequence(params AnthropicStubHandler[] steps)
    {
        var next = -1;
        return new AnthropicStubHandler(() => steps[Math.Min(Interlocked.Increment(ref next), steps.Length - 1)]._respond());
    }

    // The fixture's stream with only the first `chunks.Count` text deltas (which must be the fixture's own, in order; thinking and other frames are not counted).
    // `failWith` breaks the body after the last of them; `interruptedBy` ends it with that hand-written SSE file instead of the tail.
    public static AnthropicStubHandler Streaming(IReadOnlyList<string> chunks, Task? gate = null, Exception? failWith = null, string? interruptedBy = null)
    {
        var frames = Frames(StreamFixture);
        var deltaIndexes = frames.Select((f, i) => (f, i)).Where(x => x.f.Contains("\"type\":\"text_delta\"", StringComparison.Ordinal)).Select(x => x.i).ToArray();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (!frames[deltaIndexes[i]].Contains($"\"text\":\"{chunks[i]}\"", StringComparison.Ordinal))
            {
                throw new ArgumentException($"The stream fixture has no text delta {i} with '{chunks[i]}'.", nameof(chunks));
            }
        }

        var lastKept = chunks.Count == 0 ? 0 : deltaIndexes[chunks.Count - 1];
        var steps = new List<object>();
        for (var i = 0; i < frames.Count; i++)
        {
            var isDelta = deltaIndexes.Contains(i);
            var kept = failWith == null && interruptedBy == null ? !isDelta || i <= lastKept : i <= lastKept;
            if (kept)
            {
                steps.Add(frames[i]);
            }

            if (i == deltaIndexes[0] && gate != null)
            {
                steps.Add(gate);
            }
        }

        if (interruptedBy != null)
        {
            steps.AddRange(Frames(interruptedBy));
        }

        if (failWith != null)
        {
            steps.Add(failWith);
        }

        return new AnthropicStubHandler(() => EventStream(steps));
    }

    // A response whose whole body is the given hand-written SSE file (an `error` event, for example).
    public static AnthropicStubHandler StreamingFile(string fixture) => new(() => EventStream([.. Frames(fixture)]));

    // A documented JSON error body for a status (401, 429, 529, ...).
    public static AnthropicStubHandler Error(HttpStatusCode status, string fixture) => new(() => new HttpResponseMessage(status)
    {
        Content = new StringContent(File.ReadAllText(FixturePath(fixture)), Encoding.UTF8, "application/json"),
    });

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        RequestUrl = request.RequestUri?.AbsoluteUri;
        ApiKeyHeader = request.Headers.TryGetValues("x-api-key", out var values) ? values.FirstOrDefault() : null;
        RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond();
    }

    private static HttpResponseMessage EventStream(List<object> steps)
    {
        // A write completes only once the reader has taken it, so a failure step lands after every earlier frame was read.
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 0));
        _ = PlayAsync(steps, pipe.Writer);
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // Writes string steps as bytes, waits on Task steps, and ends the pipe with the Exception step (the reader then throws it).
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

    private static string FixturePath(string name) => Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "ai", name);

    private static List<string> Frames(string fixture) =>
        File.ReadAllText(FixturePath(fixture)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(f => f + "\n\n").ToList();
}
