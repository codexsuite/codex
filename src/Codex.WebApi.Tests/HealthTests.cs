using System.Net;
using System.Net.Http.Json;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Dtos;

namespace Codex.WebApi.Tests;

[TestFixture]
public class HealthTests : ApiFixture
{
    [Test]
    public async Task Health_WhenAppStarts_ShouldReturnOk()
    {
        var response = await Client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthDto>();
        body!.Status.ShouldBe("ok");
        body.Version.ShouldNotBeEmpty();
    }

    [Test]
    public async Task ContainerHealth_WhenCalled_ShouldReturnOk()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task UnknownApiRoute_WhenRequested_ShouldReturn404RatherThanTheSpaShell()
    {
        var response = await Client.GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
