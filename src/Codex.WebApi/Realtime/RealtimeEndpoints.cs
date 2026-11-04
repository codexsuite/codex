using Codex.Contracts;

namespace Codex.WebApi.Realtime;

public sealed class RealtimeEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapHub<BookHub>(BookHub.Path);
}
