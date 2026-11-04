using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

[TestFixture]
public class KbEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _people;
    private Guid _places;
    private Guid _anna;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        _people = await AddCategoryAsync(_book, "People", "a0", "category-amber");
        _places = await AddCategoryAsync(_book, "Places", "a1");
        _anna = await AddEntryAsync(_book, _people, "Anna", "a0", ["Annie", "A."], always: true);
        await AddEntryAsync(_book, _places, "Harbour", "a0");
        var archived = await AddEntryAsync(_book, _places, "Gone", "a1");
        var chapter = await AddChapterAsync(_book);
        await AddSceneAsync(_book, chapter, "a0", $"[Anna](kb:{_anna}) met [Anna](kb:{_anna}).");
        var two = await AddSceneAsync(_book, chapter, "a1");
        await using var connection = await OpenDatabaseAsync();
        await connection.ExecuteAsync("INSERT INTO scene_kb VALUES (@s, @e)", new { s = two, e = _anna });
        await connection.ExecuteAsync(
            "UPDATE kb_entry SET summary = @summary WHERE id = @id",
            new { id = _anna, summary = "\n  \nThe ship-builder's daughter.\nSecond line." });
        await connection.ExecuteAsync(
            "UPDATE kb_entry SET summary = @summary WHERE name = 'Harbour' AND book_id = @book",
            new { book = _book, summary = new string('x', 200) });
        await connection.ExecuteAsync("UPDATE kb_entry SET archived_at = now() WHERE id = @archived", new { archived });
    }

    [Test]
    public async Task Get_ShouldListCategoriesAndEntriesWithCounts()
    {
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!;

        kb.Categories.Select(c => c.Name).ShouldBe(["People", "Places"]);
        kb.Categories[0].Id.ShouldBe(_people);
        kb.Categories[0].Colour.ShouldBe("category-amber");
        kb.Entries.Select(e => e.Name).ShouldBe(["Anna", "Harbour"]);
        var anna = kb.Entries[0];
        anna.CategoryId.ShouldBe(_people);
        anna.Aliases.ShouldBe(["Annie", "A."]);
        anna.SceneCount.ShouldBe(2);
        anna.UseCount.ShouldBe(3);
        anna.AlwaysInContext.ShouldBeTrue();
        anna.SummaryLine.ShouldBe("The ship-builder's daughter.");
        anna.FromSeries.ShouldBeFalse();
        anna.SeriesName.ShouldBeNull();
        kb.Entries[1].SummaryLine.ShouldBe(new string('x', 139) + "…");
        kb.Entries[1].SceneCount.ShouldBe(0);
        kb.Entries[1].CategoryId.ShouldBe(_places);
    }

    [Test]
    public async Task Get_WhenUnknownBook_ShouldReturnNotFound()
    {
        var error = await ApiErrors.ReadApiErrorAsync(await Client.GetAsync($"/api/books/{Guid.NewGuid()}/kb"), System.Net.HttpStatusCode.NotFound);
        error.Code.ShouldBe("not_found");
    }
}
