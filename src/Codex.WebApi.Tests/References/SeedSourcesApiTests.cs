using System.Net;
using System.Net.Http.Json;
using Codex.TestSupport.Fixtures;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.References;
using Codex.Core;
using Dapper;

namespace Codex.WebApi.Tests.References;

// The sources table, bibliography feed and exports read the seeded Product Engineering book (issue #16 section 1.6).
[TestFixture]
public class SeedSourcesApiTests : ApiFixture
{
    private static string SourcesUrl => $"/api/books/{SeedIds.Books.ProductEngineering}/sources";

    [Test]
    public async Task Sources_WhenSeeded_ShouldListTwelveInMarkerOrderWithTheUncitedLast()
    {
        var rows = (await Client.GetFromJsonAsync<SourceRowDto[]>(SourcesUrl))!;

        rows.Length.ShouldBe(12);
        rows.Select(r => r.Number).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, null]);
        rows.Count(r => r.Status == "cleared").ShouldBe(4);
        rows.Count(r => r.Status == "risk").ShouldBe(3);
        rows.Count(r => r.Status == "needs_check").ShouldBe(4);
        var uncited = rows[^1];
        (uncited.Status, uncited.Cited, uncited.Uses, uncited.UsedIn).ShouldBe(("not_cited", false, 0, string.Empty));
    }

    [Test]
    public async Task Sources_WhenSeeded_ShouldDescribeTheVelocityTrap()
    {
        var rows = (await Client.GetFromJsonAsync<SourceRowDto[]>(SourcesUrl))!;

        var row = rows.Single(r => r.Id == SeedIds.References.VelocityTrap);

        (row.Number, row.Status, row.Reason, row.QuotedWords, row.Limit).ShouldBe((4, "risk", "quote_over_limit", 342, 300));
        (row.Title, row.Authors, row.Year, row.Cited).ShouldBe(("The Velocity Trap", "Whitlock", 2021, true));
        row.Uses.ShouldBe(2);
        row.UsedIn.ShouldStartWith("Ch ");
    }

    [Test]
    public async Task Sources_WhenSignedOff_ShouldSayWhoClearedItAndWhen()
    {
        var rows = (await Client.GetFromJsonAsync<SourceRowDto[]>(SourcesUrl))!;

        // Beck was signed off by JD (Joel Dickson) when the seed loaded.
        await using var db = await OpenDatabaseAsync();
        var signedOff = await db.ExecuteScalarAsync<DateTimeOffset>(
            "SELECT signed_off_at FROM reference WHERE id = @id", new { id = SeedIds.References.BeckXp });
        var day = DisplayDate.ShortDay(signedOff, TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone);
        rows.Single(r => r.Id == SeedIds.References.BeckXp).ClearedLabel.ShouldBe($"Cleared by JD · {day}");
        rows.Single(r => r.Id == SeedIds.References.VelocityTrap).ClearedLabel.ShouldBeNull();
    }

    [Test]
    public async Task Csl_WhenSeeded_ShouldOmitTheUncitedSource()
    {
        var items = (await Client.GetFromJsonAsync<SourceCslDto[]>($"{SourcesUrl}/csl"))!;

        items.Length.ShouldBe(11);
        items.Select(i => i.Csl.GetProperty("title").GetString()).ShouldNotContain("Lean Software Development");
        items.Single(i => i.SourceId == SeedIds.References.VelocityTrap).Status.ShouldBe("risk");
        items.ShouldAllBe(i => i.Csl.GetProperty("type").GetString() != null);
    }

    [Test]
    public async Task Export_WhenBibtex_ShouldBeAnAttachmentWithEverySource()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export?format=bibtex");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.ShouldEndWith("-sources.bib");
        var text = await response.Content.ReadAsStringAsync();
        text.Split("\n@", StringSplitOptions.None).Length.ShouldBe(12);
        text.ShouldContain("title = {{The Velocity Trap}}");
        text.ShouldContain("year = {2021}");
    }

    [Test]
    public async Task Export_WhenRis_ShouldCloseEveryRecord()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export?format=ris");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Split("ER  - \r\n").Length.ShouldBe(13);
        text.ShouldContain("TI  - The Velocity Trap\r\n");
        text.ShouldContain("PY  - 2021\r\n");
        text.ShouldContain("TY  - BOOK\r\n");
    }

    [Test]
    public async Task Export_WhenCslJson_ShouldBeAnArrayOfItems()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export?format=csljson");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.citationstyles.csl+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetArrayLength().ShouldBe(12);
    }

    [Test]
    public async Task Export_WhenPermissions_ShouldListSourcesThatNeedOrHaveARequest()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export?format=permissions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldStartWith("Source,Authors,Year");
        lines.ShouldContain(l => l.StartsWith("The Velocity Trap,Whitlock,2021", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("Team Topologies", StringComparison.Ordinal) && l.Contains(",sent,", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.StartsWith("Lean Software Development", StringComparison.Ordinal));
    }

    [Test]
    public async Task Export_WhenFormatUnknown_ShouldBeAValidationError()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export?format=docx");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
    }

    [Test]
    public async Task Export_WhenFormatMissing_ShouldBeAValidationError()
    {
        var response = await Client.GetAsync($"{SourcesUrl}/export");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
    }

    [TestCase("sources")]
    [TestCase("sources/csl")]
    [TestCase("sources/export?format=ris")]
    public async Task Sources_WhenBookUnknown_ShouldBeNotFound(string path)
    {
        var response = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/{path}");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.NotFound);

        error.Code.ShouldBe("not_found");
    }
}
