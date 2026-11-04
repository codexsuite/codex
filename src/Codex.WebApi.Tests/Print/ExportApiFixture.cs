using System.Diagnostics;
using System.Net.Http.Json;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Print;

// Shared by the export tests: waiting for the in-process export worker by polling the API.
public abstract class ExportApiFixture : ApiFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(240);

    // Polls the export until the worker has settled it.
    protected async Task<ExportDto> SettledAsync(Guid exportId)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var export = (await Client.GetFromJsonAsync<ExportDto>($"/api/exports/{exportId}"))!;
            if (export.Status is "succeeded" or "failed" or "cancelled")
            {
                return export;
            }
            clock.Elapsed.ShouldBeLessThan(Patience, "the export worker did not finish");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}
