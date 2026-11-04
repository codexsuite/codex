using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;

namespace Codex.WebApi.Tests.KnowledgeBase;

[TestFixture]
public class KbSeriesEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _otherBook;
    private Guid _ownPeople;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var series = await AddSeriesAsync("The Tide Cycle");
        _book = await AddBookAsync("Tides", series);
        _otherBook = await AddBookAsync("Elsewhere");
        var seriesPeople = await AddCategoryAsync(series, "People", "a0", "category-blue");
        _ownPeople = await AddCategoryAsync(_book, "People", "a0");
        await AddCategoryAsync(_book, "Places", "a1");
        await AddCategoryAsync(series, "Factions", "a2");
        await AddEntryAsync(series, seriesPeople, "Captain", "a0");
        await AddEntryAsync(_book, _ownPeople, "Anna", "a1");
        await AddEntryAsync(_otherBook, await AddCategoryAsync(_otherBook, "People"), "Stranger");
    }

    [Test]
    public async Task Get_WhenSeriesBook_ShouldIncludeSeriesEntries()
    {
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{_book}/kb"))!;

        kb.Categories.Select(c => c.Name).ShouldBe(["People", "Places", "Factions"]);
        kb.Categories[0].Id.ShouldBe(_ownPeople); // same-named categories merge, the book's own id wins
        kb.Categories[0].Colour.ShouldBe("category-blue"); // the series' look wins
        kb.Entries.Select(e => e.Name).ShouldBe(["Captain", "Anna"]);
        kb.Entries.ShouldAllBe(e => e.CategoryId == _ownPeople);
        kb.Entries[0].FromSeries.ShouldBeTrue();
        kb.Entries[0].SeriesName.ShouldBe("The Tide Cycle");
        kb.Entries[1].FromSeries.ShouldBeFalse();
        kb.Categories.Select(c => c.FromSeries).ShouldBe([false, false, true]); // Factions exists only in the series
        kb.Categories.Select(c => c.SeriesName).ShouldBe([null, null, "The Tide Cycle"]);
    }
}
