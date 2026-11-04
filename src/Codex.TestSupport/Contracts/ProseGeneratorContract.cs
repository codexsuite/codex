using Shouldly;
using Codex.Contracts.Ai;

namespace Codex.TestSupport.Contracts;

// Every IProseGenerator implementation inherits this and supplies Create(script).
public abstract class ProseGeneratorContract
{
    // A provider that ignores Stop would hang here; the timeout turns that into a failure (a TimeoutException, not the expected cancellation).
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly ProseRequest Request = new("claude-sonnet-5-5", "Write plainly.", "The door opened slowly.");

    protected abstract IProseGenerator Create(ProseScript script);

    [Test]
    public async Task GenerateAsync_WhenScripted_ShouldStreamChunksInOrderThenFinalResult()
    {
        var generator = Create(new ProseScript(["The ", "door ", "creaked."]));

        var chunks = await Collect(generator, CancellationToken.None);

        chunks.Take(3).Select(c => c.Text).ShouldBe(["The ", "door ", "creaked."]);
        chunks.Take(3).ShouldAllBe(c => c.Final == null);
        chunks.Count.ShouldBe(4);
        var final = chunks[3].Final.ShouldNotBeNull();
        final.Model.ShouldBe(Request.Model);
        final.InputTokens.ShouldBeGreaterThan(0);
        final.OutputTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task GenerateAsync_WhenCancelledMidStream_ShouldThrowAndKeepPartialText()
    {
        var never = new TaskCompletionSource();
        var generator = Create(new ProseScript(["The ", "door ", "creaked."], Gate: never.Task));
        using var stop = new CancellationTokenSource();
        var received = new List<string>();

        var stream = generator.GenerateAsync(Request, stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            (await MoveNextWithinAsync(stream)).ShouldBeTrue();
            received.Add(stream.Current.Text);
            await stop.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(async () => await MoveNextWithinAsync(stream));
        }
        finally
        {
            await stream.DisposeAsync();
        }

        string.Concat(received).ShouldBe("The ");
    }

    [Test]
    public async Task GenerateAsync_WhenProviderFails_ShouldSurfaceFailureAfterEarlierChunks()
    {
        var generator = Create(new ProseScript(["The ", "door "], FailWith: new InvalidOperationException("provider down")));
        var received = new List<string>();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var chunk in generator.GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        failure.Message.ShouldBe("provider down");
        received.ShouldBe(["The ", "door "]);
    }

    private static async Task<bool> MoveNextWithinAsync(IAsyncEnumerator<ProseChunk> stream)
    {
        var move = Task.Run(async () => await stream.MoveNextAsync());
        if (await Task.WhenAny(move, Task.Delay(Patience)) != move)
        {
            throw new TimeoutException($"The stream did not answer within {Patience.TotalSeconds:0} s.");
        }

        return await move;
    }

    private static async Task<List<ProseChunk>> Collect(IProseGenerator generator, CancellationToken cancellationToken)
    {
        var chunks = new List<ProseChunk>();
        await foreach (var chunk in generator.GenerateAsync(Request, cancellationToken))
        {
            chunks.Add(chunk);
        }
        return chunks;
    }
}
