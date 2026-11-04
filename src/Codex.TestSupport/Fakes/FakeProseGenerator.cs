using System.Runtime.CompilerServices;
using Codex.Contracts.Ai;

namespace Codex.TestSupport.Fakes;

// A deterministic IProseGenerator: it streams the scripted chunks, then a final chunk with word-count tokens.
// `gate` holds the stream after the first chunk until the task completes (or the caller cancels), so a test
// can Stop mid-stream without sleeping. `failWith` is thrown after the last chunk has been streamed.
public sealed class FakeProseGenerator(
    IReadOnlyList<string> chunks,
    Task? gate = null,
    Exception? failWith = null) : IProseGenerator
{
    private readonly List<ProseRequest> _requests = [];

    public IReadOnlyList<ProseRequest> Requests => _requests;

    public async IAsyncEnumerable<ProseChunk> GenerateAsync(
        ProseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _requests.Add(request);
        var outputWords = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outputWords += Words(chunks[i]);
            yield return new ProseChunk(chunks[i]);
            if (i == 0 && gate != null)
            {
                await HoldUntilReleasedAsync(gate, cancellationToken);
            }
        }
        if (failWith != null)
        {
            throw failWith;
        }
        yield return ProseChunk.Done(new ProseResult(request.Model, Words(request.Instruction) + Words(request.Prompt), outputWords));
    }

    private static async Task HoldUntilReleasedAsync(Task gate, CancellationToken cancellationToken)
    {
        var stopped = new TaskCompletionSource();
        await using var registration = cancellationToken.Register(() => stopped.TrySetResult());
        await Task.WhenAny(gate, stopped.Task);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static int Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}
