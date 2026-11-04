using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// Concurrent creates of one category name (in any case) make exactly one category; the others are 409, never 500.
[TestFixture]
public class KbCategoryRaceTests : BookContentFixture
{
    [Test]
    public async Task Create_WhenTheSameNameIsCreatedConcurrently_ShouldMakeOneAndRefuseTheRest()
    {
        var book = await AddBookAsync("Racing categories");

        var responses = await Task.WhenAll(new[] { "Places", "places", "PLACES", "Places" }.Select(name =>
            Client.PostAsJsonAsync($"/api/books/{book}/kb/categories", new CreateKbCategoryRequest(name, "category-law"))));

        responses.Select(r => r.StatusCode).Order().ShouldBe(
            [HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Conflict]);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_category WHERE book_id = @book", new { book })).ShouldBe(1);
    }
}
