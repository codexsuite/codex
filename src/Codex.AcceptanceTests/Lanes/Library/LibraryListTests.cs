using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// The list view from the seed; the view, group and sort live in the URL.
[TestFixture]
public class LibraryListTests : SeededAcceptanceTest
{
    private LibraryScreen Library => new(Page);

    [Test, Order(1)]
    public async Task List_WhenGroupedBySeries_ShouldShowGroupRows()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("view", "list"), ("sort", "title")));

        await Expect(Library.Shelf.ListView).ToBeVisibleAsync();
        await Expect(Library.GroupRows).ToHaveCountAsync(4);
        await Expect(Library.GroupRows.Nth(0)).ToContainTextAsync("Forsaken Gods · 3");
        await Expect(Library.GroupRows.Nth(1)).ToContainTextAsync("The Ashen Crown · 4");
        await Expect(Library.GroupRows.Nth(2)).ToContainTextAsync("Tidewater Chronicles · 2");
        await Expect(Library.GroupRows.Nth(3)).ToContainTextAsync("Standalone · 6");
    }

    [Test, Order(2)]
    public async Task List_WhenSortedByLastEdited_ShouldPutProductEngineeringFirst()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("view", "list"), ("group", "none")));

        await Expect(Library.GroupRows).ToHaveCountAsync(0);
        await Expect(Library.Shelf.ListView).ToContainTextAsync(new Regex(@"Edited\s*Product Engineering"));
        await Expect(Library.Shelf.SortControl).ToHaveValueAsync("lastEdited");
    }

    [Test, Order(3)]
    public async Task List_WhenSortedByTitle_ShouldReorderAndKeepUrlState()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("view", "list"), ("group", "none")));

        await Expect(Library.Shelf.ListView).ToBeVisibleAsync();
        var response = await Page.RunAndWaitForApiAsync("/api/library", "GET",
            () => Library.Shelf.SortControl.SelectOptionAsync("title"));

        response.Url.ShouldContain("sort=title");
        await Expect(Page).ToHaveURLAsync(new Regex("sort=title"));
        await Expect(Library.Shelf.ListView).Not.ToContainTextAsync(new Regex(@"Edited\s*Product Engineering"));
    }
}
