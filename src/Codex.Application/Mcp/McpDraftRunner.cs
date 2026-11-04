using System.Collections.Concurrent;
using Agoda.IoC.Core;
using Codex.Application.Features.Ai;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Mcp;

// Runs draft_prose's generations in the background (M5). StartAsync returns once the draft exists (DraftGenerator has
// created the ai_generation row and the model has begun to answer), so the tool can hand back the generation id at
// once; a task owned by this singleton then drains the rest of the stream through the same DraftGenerator the Write
// screen uses, which stores the text and moves the row to pending (or failed). Nothing here applies a draft (M12).
//
// This is a plain background task, not a hosted service: the stdio host removes every IHostedService (Codex.Mcp's
// Program.cs), and the web host's /mcp serves the same tools. Disposing the host cancels the running drafts, which
// DraftGenerator closes as stopped (the text so far is kept).
[RegisterSingleton]
public sealed class McpDraftRunner(
    DraftGenerator generator, McpClientDrafts clientDrafts, ICurrentUser currentUser, ILogger<McpDraftRunner> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    // The generation's `started` line, once the draft exists. A refusal before that (unknown template, a model that is not valid, no AI
    // integration, a beat that is gone, a model that fails before its first chunk) is thrown to the caller, as the HTTP API does.
    public async Task<DraftStreamStarted> StartAsync(
        Guid? beatId, Guid? sceneId, GenerateDraftRequest request, McpCaller caller, CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource<DraftStreamStarted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.CreateVersion7();
        // The draft outlives the /mcp request whose user it belongs to, so it carries that user with it.
        var userId = currentUser.Id;
        var run = Task.Run(async () =>
        {
            using (CurrentUser.ActAs(userId))
            {
                await RunAsync(id, beatId, sceneId, request, caller, started);
            }
        }, CancellationToken.None);
        _running[id] = run;
        if (run.IsCompleted)
        {
            _running.TryRemove(id, out _);
        }
        await using var registration = cancellationToken.Register(() => started.TrySetCanceled(cancellationToken));
        return await started.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        var finished = Task.WhenAll(_running.Values);
        if (await Task.WhenAny(finished, Task.Delay(ShutdownWait, CancellationToken.None)) != finished)
        {
            logger.LogWarning("Drafts still running at shutdown were left to the database to close.");
        }
        _stop.Dispose();
    }

    private async Task RunAsync(
        Guid runId, Guid? beatId, Guid? sceneId, GenerateDraftRequest request, McpCaller caller, TaskCompletionSource<DraftStreamStarted> started)
    {
        try
        {
            var events = await (beatId is { } beat
                ? generator.GenerateForBeatAsync(beat, request, _stop.Token)
                : generator.ContinueSceneAsync(sceneId!.Value, request, _stop.Token));
            await foreach (var line in events.WithCancellation(_stop.Token))
            {
                if (line.Started is { } s)
                {
                    await clientDrafts.MarkAsync(s.GenerationId, caller, _stop.Token);
                    started.TrySetResult(s);
                }
                else if (line.Error is { } error)
                {
                    logger.LogWarning("MCP draft {Run} failed: {Message}", runId, error.Error);
                }
            }
        }
        catch (Exception e) when (!started.Task.IsCompleted)
        {
            started.TrySetException(e);
        }
        catch (Exception e)
        {
            logger.LogError(e, "MCP draft {Run} stopped with an error", runId);
        }
        finally
        {
            started.TrySetCanceled(CancellationToken.None);
            _running.TryRemove(runId, out _);
        }
    }
}
