using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Print.Templates;

// The built-in template sync runs at startup, after the migration service (DatabaseServices sorts first by module name).
public sealed class TemplateServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
        => services.AddHostedService<BuiltInTemplateSync>();
}
