using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Identity;

// AdminBootstrap is a hosted service, so it is registered here rather than by attribute: module order (by full type
// name) puts Codex.Application.Identity after Codex.Application.Database, so it starts after the migrations have run.
public sealed class IdentityServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
        => services.AddHostedService<AdminBootstrap>();
}
