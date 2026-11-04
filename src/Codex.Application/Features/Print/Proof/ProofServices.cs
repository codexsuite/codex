using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Print.Proof;

// The proof worker is one hosted service in every process; it claims jobs only where CodexRole.RunsJobs.
public sealed class ProofServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ProofWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ProofWorker>());
    }
}
