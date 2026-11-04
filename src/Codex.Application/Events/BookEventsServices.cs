using Codex.Contracts;
using Codex.Contracts.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Events;

public sealed class BookEventsServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<BookEventsNotifier>();
        services.AddSingleton<InMemoryBookEvents>();
        services.AddSingleton<IBookEvents>(sp => sp.GetRequiredService<InMemoryBookEvents>());
        services.AddSingleton(sp => BookEventsSettings.From(sp.GetRequiredService<IConfiguration>()));
        services.AddHostedService<BookEventsListener>();
    }
}
