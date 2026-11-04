using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// Writes to entries and categories, in order, on one cloned schema.
[TestFixture]
public class KbEntryEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _otherBook;
    private Guid _people;
    private Guid _places;
    private Guid _otherCategory;
    private Guid _anna;
    private Guid _created;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        _otherBook = await AddBookAsync("Elsewhere");
        _people = await AddCategoryAsync(_book, "People", "a0");
        _places = await AddCategoryAsync(_book, "Places", "a1");
        _otherCategory = await AddCategoryAsync(_otherBook, "People");
        _anna = await AddEntryAsync(_book, _people, "Anna", "a0", ["Annie"]);
        await AddEntryAsync(_book, _people, "Ben", "a1", ["Benny"]);
        await AddEntryAsync(_book, _people, "Cora", "a2");
    }

    [Test, Order(1)]
    public async Task Post_ShouldCreateEntryLastInItsCategory()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_people, "  Dara "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        _created = entry.Id;
        entry.Name.ShouldBe("Dara");
        entry.Slug.ShouldBe("dara");
        (entry.PositionInCategory, entry.CategoryEntryCount).ShouldBe((4, 4));
    }

    [Test, Order(2)]
    public async Task Patch_ShouldChangeTextAndAliases()
    {
        var response = await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(
            "Dara Vey", "A pilot.", "Long text.", null, [new KbCustomFieldDto("URL", "https://example.test")], ["D", "d", " Dee "], true, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        (entry.Name, entry.Slug, entry.Summary, entry.Body, entry.AlwaysInContext).ShouldBe(("Dara Vey", "dara", "A pilot.", "Long text.", true));
        entry.Aliases.ShouldBe(["D", "Dee", "Dara"]);
        entry.CustomFields.ShouldBe([new KbCustomFieldDto("URL", "https://example.test")]);
    }

    [Test, Order(3)]
    public async Task Patch_WhenAliasIsAnotherEntrysNameOrAlias_ShouldConflict()
    {
        foreach (var alias in new[] { "ben", "BENNY" })
        {
            var error = await ApiErrors.ReadApiErrorAsync(
                await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(null, null, null, null, null, [alias], null, null)),
                HttpStatusCode.Conflict);

            error.Code.ShouldBe("conflict");
            error.Error.ShouldContain("Ben");
        }
        (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{_created}"))!.Aliases.ShouldBe(["D", "Dee", "Dara"]);
    }

    [Test, Order(4)]
    public async Task Patch_WhenValidationFails_ShouldReturn400()
    {
        var foreign = await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(null, null, null, null, null, null, null, _otherCategory));
        var field = await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(null, null, null, new Dictionary<string, string> { ["born"] = "x" }, null, null, null, null));
        var blank = await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(" ", null, null, null, null, null, null, null));
        var own = await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}", new PatchKbEntryRequest(null, null, null, null, null, ["dara vey"], null, null));

        foreach (var response in new[] { foreign, field, blank, own })
        {
            (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        }
    }

    [Test, Order(5)]
    public async Task Patch_WhenCategoryChanged_ShouldMoveEntryLast()
    {
        var entry = (await (await Client.PatchAsJsonAsync($"/api/kb/entries/{_created}",
            new PatchKbEntryRequest(null, null, null, null, null, null, null, _places))).Content.ReadFromJsonAsync<KbEntryDto>())!;

        (entry.CategoryName, entry.PositionInCategory, entry.CategoryEntryCount).ShouldBe(("Places", 1, 1));
    }

    [Test, Order(6)]
    public async Task PutPosition_ShouldReorderWithinCategory()
    {
        var ben = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!.Entries.Single(e => e.Name == "Ben").Id;

        (await Client.PutAsJsonAsync($"/api/kb/entries/{_anna}/position", new MoveKbEntryRequest(ben))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!.Entries
            .Where(e => e.CategoryId == _people).Select(e => e.Name).ShouldBe(["Ben", "Anna", "Cora"]);

        (await Client.PutAsJsonAsync($"/api/kb/entries/{_anna}/position", new MoveKbEntryRequest(null))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!.Entries
            .Where(e => e.CategoryId == _people).Select(e => e.Name).ShouldBe(["Anna", "Ben", "Cora"]);
    }

    [Test, Order(7)]
    public async Task PutPosition_WhenAnchorIsInAnotherCategory_ShouldReturn400()
    {
        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PutAsJsonAsync($"/api/kb/entries/{_anna}/position", new MoveKbEntryRequest(_created)), HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
    }

    [Test, Order(8)]
    public async Task PostEntry_WhenCategoryOfAnotherBookOrBlankName_ShouldReject()
    {
        var foreign = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_otherCategory, "Spy"));
        var blank = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_people, "  "));
        var unknownBook = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/kb/entries", new CreateKbEntryRequest(_people, "X"));

        (await ApiErrors.ReadApiErrorAsync(foreign, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(blank, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(unknownBook, HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_entry WHERE book_id = @book", new { book = _book })).ShouldBe(4);
    }

    [Test, Order(9)]
    public async Task Get_WhenUnknownEntry_ShouldReturn404()
    {
        (await ApiErrors.ReadApiErrorAsync(await Client.GetAsync($"/api/kb/entries/{Guid.NewGuid()}"), HttpStatusCode.NotFound))
            .Code.ShouldBe("not_found");
        (await ApiErrors.ReadApiErrorAsync(
            await Client.PatchAsJsonAsync($"/api/kb/entries/{Guid.NewGuid()}", new PatchKbEntryRequest("x", null, null, null, null, null, null, null)),
            HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(10)]
    public async Task PostCategory_ShouldCreateAndRejectBlankBadColourAndDuplicate()
    {
        var created = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest("Factions", "category-law"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var category = (await created.Content.ReadFromJsonAsync<KbCategoryDto>())!;
        (category.Name, category.Colour, category.FromSeries).ShouldBe(("Factions", "category-law", false));
        category.FieldDefs.ShouldBeEmpty();
        (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!.Categories.Select(c => c.Name).ShouldBe(["People", "Places", "Factions"]);

        var blank = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest("  ", "category-law"));
        var colour = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest("Ships", "red"));
        var duplicate = await Client.PostAsJsonAsync($"/api/books/{_book}/kb/categories", new CreateKbCategoryRequest("people", "category-law"));

        (await ApiErrors.ReadApiErrorAsync(blank, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(colour, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await ApiErrors.ReadApiErrorAsync(duplicate, HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
    }

    [Test, Order(11)]
    public async Task Post_WhenCreatedConcurrently_ShouldGiveEveryEntryItsOwnPlace()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Client.PostAsJsonAsync($"/api/books/{_book}/kb/entries", new CreateKbEntryRequest(_places, $"Harbour {i}"))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        await using var connection = await OpenDatabaseAsync();
        var positions = (await connection.QueryAsync<string>(
            "SELECT position FROM kb_entry WHERE category_id = @category", new { category = _places })).ToList();
        positions.Count.ShouldBeGreaterThanOrEqualTo(8);
        positions.Distinct().Count().ShouldBe(positions.Count);
    }
}
