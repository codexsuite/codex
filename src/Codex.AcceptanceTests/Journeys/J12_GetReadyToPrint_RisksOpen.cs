using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Layout;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.AcceptanceTests.Journeys;

// J12 · Get ready to print, the blocked case from the #16 review (section B): Export for printer while the three
// copyright risks are still open. The preflight lists them under "Blocks export" and Queue export stays disabled; the
// server refuses a final export with 422 blocked; the only way through is a draft (DRAFT on every page), which queues
// with the blocks kept on the job's preflight and settings.draft set. The app has no override that sends a final
// export, and no separate override log: the draft flag and the recorded blocks on the export row are the trace.
[TestFixture, Category("J12")]
public class J12_GetReadyToPrint_RisksOpen : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    private LayoutScreen Layout => new(Page);

    private static ExportRequest Request(bool draft) =>
        new("novel-6x9", "6x9", null, "0.125in", false, "print", "pdf", draft);

    [Test, Order(1)]
    public async Task Step1_WhenExportForPrinterOpenedWithRisksOpen_ShouldListThreeBlocksAndDisableQueue()
    {
        await Page.GotoAsync(Links.Layout(BookId));
        await Layout.ExportOpen.ClickAsync();
        await Expect(Layout.ExportDialog).ToBeVisibleAsync();

        await Expect(Layout.ExportVerdict).ToHaveTextAsync("3 issues block export");
        await Expect(Layout.PreflightItems).ToHaveCountAsync(3);
        await Expect(Layout.ExportPreflight).ToContainTextAsync("The Velocity Trap: copyright risk (quote over limit)");
        await Expect(Layout.ExportPreflight).ToContainTextAsync("Team Topologies");
        await Expect(Layout.ExportPreflight).ToContainTextAsync("Ship It Friday");
        await Expect(Layout.ExportQueue).ToBeDisabledAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenAFinalExportIsPostedWithRisksOpen_ShouldBeRefusedAsBlockedAndQueueNothing()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        var response = await client.PostAsJsonAsync($"/api/books/{BookId}/exports", Request(false));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.ShouldBe(ApiErrorCodes.Blocked);
        error.Error.ShouldBe("3 issues block export. Fix them or export a draft.");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM export_job WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(0);
    }

    [Test, Order(3)]
    public async Task Step3_WhenDraftIsTicked_ShouldQueueADraftThatKeepsTheBlocksOnItsRow()
    {
        await Page.GotoAsync(Links.Layout(BookId));
        await Layout.ExportOpen.ClickAsync();
        await Expect(Layout.ExportQueue).ToBeDisabledAsync();
        await Layout.ExportBleed("0.125in").ClickAsync();
        await Layout.ExportDraft.CheckAsync();
        await Expect(Layout.ExportQueue).ToBeEnabledAsync();

        var queued = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/exports", "POST", () => Layout.ExportQueue.ClickAsync());

        queued.Status.ShouldBe(202);
        await Expect(Layout.ExportItems.Or(Layout.ExportRunning)).ToHaveCountAsync(1);
        (await ScalarSqlAsync<bool>("SELECT (settings ->> 'draft')::boolean FROM export_job WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBeTrue();
        (await ScalarSqlAsync<int>("SELECT jsonb_array_length(preflight -> 'blocks') FROM export_job WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(3);
    }
}
