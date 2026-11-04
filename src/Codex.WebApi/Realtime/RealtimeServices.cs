using Codex.Contracts;

namespace Codex.WebApi.Realtime;

public sealed class RealtimeServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR();
        services.AddSingleton<BookGroupMembers>();
        services.AddHostedService<BookEventsRelay>();
    }
}
