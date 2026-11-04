using System.Net;
using System.Net.Http.Json;
using Codex.Application;
using Codex.Application.Database;
using Codex.Application.Mcp;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Features.Auth;
using Codex.WebApi.Infrastructure;
using Codex.WebApi.Realtime;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.WebApi.Tests;

[TestFixture]
public class ModuleScanTests : ApiFixture
{
    [Test]
    public void Scan_WhenHealthModuleInProductionAssembly_ShouldFindIt()
    {
        var modules = ModuleScan.Find<IEndpointModule>([ApiMarker.Assembly, ApplicationMarker.Assembly]);

        modules.Select(m => m.GetType()).ShouldContain(typeof(HealthEndpoints));
    }

    // Feature endpoints are controllers (#98); IEndpointModule is only for what MVC does not map: health, the hub, /mcp
    // and the /login page fallback.
    [Test]
    public void Scan_WhenEndpointModulesFound_ShouldBeOnlyTheNonMvcEndpoints()
    {
        var modules = ModuleScan.Find<IEndpointModule>([ApiMarker.Assembly, ApplicationMarker.Assembly]);

        modules.Select(m => m.GetType()).ShouldBe([typeof(HealthEndpoints), typeof(RealtimeEndpoints), typeof(McpHttpEndpoints), typeof(LoginPageEndpoints)], ignoreOrder: true);
    }

    [Test]
    public void Endpoints_UnderApi_ShouldAllBeControllerActionsExceptHealthAndTheFallback()
    {
        var notControllers = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => ("/" + e.RoutePattern.RawText?.TrimStart('/')).StartsWith("/api/", StringComparison.Ordinal))
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Select(e => e.RoutePattern.RawText);

        notControllers.ShouldBe(["/api/health", "/api/{**path}"], ignoreOrder: true);
    }

    [Test]
    public void Scan_WhenServiceModuleInApplicationAssembly_ShouldFindIt()
    {
        var modules = ModuleScan.Find<IServiceModule>([ApiMarker.Assembly, ApplicationMarker.Assembly]);

        modules.Select(m => m.GetType()).ShouldContain(typeof(DatabaseServices));
    }

    [Test]
    public async Task Program_WhenModulesScanned_ShouldMapTheirEndpointsAndRegisterTheirServices()
    {
        var response = await Client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Host.Services.GetRequiredService<NpgsqlDataSource>().ConnectionString.ShouldContain("codex_t_");
        (await Client.GetFromJsonAsync<MeDto>("/api/auth/me"))!.Id.ShouldBe(SeedIds.Users.Jd);
    }
}
