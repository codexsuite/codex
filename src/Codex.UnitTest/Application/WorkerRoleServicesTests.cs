using System.Reflection;
using Codex.Application;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.WebApi;
using Codex.WebApi.Infrastructure;
using Codex.WebApi.Realtime;
using Codex.WebApi.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.UnitTest.Application;

[TestFixture]
public class WorkerRoleServicesTests
{
    [Test]
    public void ModuleScan_WhenProgramFindsTheServiceModules_ShouldRunTheRoleGateLast()
    {
        Assembly[] assemblies = [ApiMarker.Assembly, ApplicationMarker.Assembly];

        ModuleScan.Find<IServiceModule>(assemblies)[^1].ShouldBeOfType<WorkerRoleServices>();
    }

    [TestCase(null, true)]
    [TestCase("web", true)]
    [TestCase("all", true)]
    [TestCase("worker", false)]
    public void RoleGate_WhenTheProcessHasARole_ShouldStartTheWebOnlyServicesOnlyOutsideTheWorker(string? role, bool webOnlyRuns)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [CodexRole.Key] = role })
            .Build();

        var relay = new RoleGatedHostedService(new BookEventsRelay(null!, null!, new BookGroupMembers()), configuration);
        var other = new RoleGatedHostedService(new StubHostedService(), configuration);

        relay.Runs.ShouldBe(webOnlyRuns);
        other.Runs.ShouldBeTrue();
    }

    [Test]
    public void AddServices_WhenHostedServicesAreRegistered_ShouldReplaceEachOneWithAGatedFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHostedService<StubHostedService>();
        services.AddSingleton<OtherHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<OtherHostedService>());

        new WorkerRoleServices().AddServices(services, new ConfigurationBuilder().Build());

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hosted.Count.ShouldBe(2);
        hosted.ShouldAllBe(d => d.ImplementationFactory != null && d.ImplementationType == null);
    }

    private sealed class StubHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
