using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Print.Export;

// The export worker is one hosted service in every process; it claims jobs only where CodexRole.RunsJobs.
public sealed class ExportServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ExportWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ExportWorker>());
    }
}
