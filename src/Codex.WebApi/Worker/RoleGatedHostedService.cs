using Codex.Application.Features.Print.Render;

namespace Codex.WebApi.Worker;

// Starts the wrapped hosted service unless this process is the worker and the service is web-only.
public sealed class RoleGatedHostedService(IHostedService inner, IConfiguration configuration) : IHostedService
{
    public IHostedService Inner { get; } = inner;

    public bool Runs => CodexRole.Of(configuration) != CodexRole.Worker || !WorkerRoleServices.WebOnly.Contains(Inner.GetType());

    public Task StartAsync(CancellationToken cancellationToken) => Runs ? Inner.StartAsync(cancellationToken) : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Runs ? Inner.StopAsync(cancellationToken) : Task.CompletedTask;
}
