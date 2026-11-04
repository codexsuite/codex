using Microsoft.Extensions.Hosting;

namespace Codex.Application.Features.Print.Render;

// Sweeps stale render job folders once at startup (RenderWorkFolders.SweepStale).
public sealed class RenderWorkSweeper(RenderWorkFolders folders) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        folders.SweepStale();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
