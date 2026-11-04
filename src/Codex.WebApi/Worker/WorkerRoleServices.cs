using Codex.Application.Features.Print.Render;
using Codex.Application.Features.References;
using Codex.Contracts;
using Codex.WebApi.Realtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codex.WebApi.Worker;

// In the worker process (CODEX_ROLE=worker, set by compose on the worker service) the hosted services that only the
// web host needs are registered but never started: the SignalR relay (no browser connects to the worker) and the
// quote-flag scanner (prose is saved through the web host, whose scanner handles it). Everything else runs as in
// the web host: migrations (off by CODEX_DB_MIGRATE=false), the asset rendition worker, and the job runners, which
// check CodexRole.RunsJobs themselves.
// It wraps every hosted service registered before it, so it must run last: ModuleScan orders modules by full type
// name and Codex.WebApi.Worker sorts after every other module namespace (WorkerRoleServicesTests pins that).
public sealed class WorkerRoleServices : IServiceModule
{
    public static readonly IReadOnlyList<Type> WebOnly = [typeof(BookEventsRelay), typeof(QuoteFlagScanner)];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        foreach (var descriptor in hosted)
        {
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Singleton<IHostedService>(sp => new RoleGatedHostedService(
                Resolve(sp, descriptor), sp.GetRequiredService<IConfiguration>())));
        }
    }

    private static IHostedService Resolve(IServiceProvider sp, ServiceDescriptor descriptor) => descriptor switch
    {
        { ImplementationInstance: IHostedService instance } => instance,
        { ImplementationFactory: { } factory } => (IHostedService)factory(sp),
        _ => (IHostedService)ActivatorUtilities.GetServiceOrCreateInstance(sp, descriptor.ImplementationType!),
    };
}
