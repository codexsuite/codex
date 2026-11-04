using Microsoft.AspNetCore.Routing;

namespace Codex.Contracts;

// A feature's HTTP surface. Program.cs finds every implementation in Codex.WebApi and
// Codex.Application by assembly scan and calls MapEndpoints once, so adding a feature
// never means editing Program.cs. Implementations need a public parameterless constructor.
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
