using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Contracts;

// A feature's service registrations, found by the same assembly scan as IEndpointModule.
// Read configuration inside a factory lambda (sp => sp.GetRequiredService<IConfiguration>()),
// not from the configuration argument: WebApplicationFactory test overrides are appended
// after registration code has run and would be ignored.
public interface IServiceModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);
}
