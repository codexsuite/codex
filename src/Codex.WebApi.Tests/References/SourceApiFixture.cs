using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.References;

// Seed-clone base for the source detail panel's endpoint tests: typed reads and writes of one source.
public abstract class SourceApiFixture : ApiFixture
{
    protected async Task<SourceDetailDto> GetSourceAsync(Guid id) =>
        (await Client.GetFromJsonAsync<SourceDetailDto>($"/api/references/{id}"))!;

    protected async Task<SourceDetailDto> PostSourceAsync(Guid id, string action, object? body = null)
    {
        using var response = await Client.PostAsJsonAsync($"/api/references/{id}/{action}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
    }

    protected async Task<ClearanceSummaryDto> GetSummaryAsync() =>
        (await Client.GetFromJsonAsync<ClearanceSummaryDto>($"/api/books/{Codex.Contracts.SeedIds.Books.ProductEngineering}/references/summary"))!;
}
