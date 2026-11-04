using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.Contracts.Jobs;
using Codex.Contracts.References;
using Codex.Contracts.Settings;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Lanes.Settings;

// Archive & export on Product Engineering: duplicate it (no references), queue a source export, then archive the copy
// and restore it. One story on one database.
[TestFixture]
public class SettingsArchiveTests : SeededAcceptanceTest
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static string _copy = "";

    private SettingsScreen Settings => new(Page);

    private async Task<T> GetAsync<T>(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<T>(path))!;
    }

    private static async Task<T> BodyAsync<T>(IResponse response) =>
        JsonSerializer.Deserialize<T>(await response.TextAsync(), JsonSerializerOptions.Web)!;

    private async Task<bool> InLibraryAsync(string id)
    {
        var library = await GetAsync<LibraryDto>("/api/library");
        return library.Standalone.Concat(library.Series.SelectMany(s => s.Books)).Any(b => b.Id.ToString() == id);
    }

    [Test, Order(1)]
    public async Task Duplicate_ShouldHaveNoReferences()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("archive", null))));
        await Settings.Duplicate.ClickAsync();
        await Expect(Settings.DuplicateTitle).ToHaveValueAsync("Product Engineering (copy)");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/duplicate", "POST",
            () => Settings.DuplicateConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        var copy = (await BodyAsync<DuplicatedBookDto>(response));
        _copy = copy.Id.ToString();
        await Expect(Settings.DuplicateDone).ToContainTextAsync("Product Engineering (copy)");
        var summary = await GetAsync<ClearanceSummaryDto>($"/api/books/{_copy}/references/summary");
        summary.Total.ShouldBe(0);
        (await GetAsync<ClearanceSummaryDto>($"/api/books/{Book}/references/summary")).Total.ShouldBe(12);
    }

    [Test, Order(2)]
    public async Task ExportSource_ShouldQueueAJob()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("archive", null))));

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/export-source", "POST",
            () => Settings.ExportSource.ClickAsync());

        response.Status.ShouldBe(200);
        var job = (await BodyAsync<JobDto>(response));
        (job.Kind, job.State).ShouldBe(("source", "queued"));
        await Expect(Settings.ExportStatus).ToHaveTextAsync("Export queued.");
        // The worker may already have picked the job up by now: it only has to exist and not have failed.
        (await GetAsync<JobDto>($"/api/jobs/{job.Id}")).State.ShouldBeOneOf("queued", "running", "done");
    }

    [Test, Order(3)]
    public async Task Archive_ShouldHideFromLibrary()
    {
        (await InLibraryAsync(_copy)).ShouldBeTrue();
        await Page.GotoAsync(Links.Settings(_copy, Links.Hash(("archive", null))));
        await Settings.Archive.ClickAsync();
        await Expect(Settings.ArchiveDialog).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{_copy}/archive", "POST",
            () => Settings.ArchiveConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Settings.ArchivedBar).ToBeVisibleAsync();
        (await InLibraryAsync(_copy)).ShouldBeFalse();
        var archived = await GetAsync<LibraryDto>("/api/library?status=archived");
        archived.Standalone.ShouldContain(b => b.Id.ToString() == _copy);
    }

    [Test, Order(4)]
    public async Task Archive_WhenAlreadyArchived_ShouldBeAConflict()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        var response = await client.PostAsync($"/api/books/{_copy}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(5)]
    public async Task Restore_ShouldBringItBack()
    {
        await Page.GotoAsync(Links.Settings(_copy, Links.Hash(("archive", null))));
        await Expect(Settings.ArchivedBar).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{_copy}/restore", "POST",
            () => Settings.Restore.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Settings.ArchivedBar).ToHaveCountAsync(0);
        (await InLibraryAsync(_copy)).ShouldBeTrue();
    }
}
