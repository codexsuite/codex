using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// A refusal keeps the risk and the answer on record; a fresh request can be drafted afterwards.
[TestFixture]
public class PermissionRefusedTests : PermissionApiFixture
{
    private static readonly Guid RequestId = SeedIds.PermissionRequests.TeamTopologies;
    private static Guid _redrafted;

    [Test, Order(1)]
    public async Task Refuse_ShouldKeepTheRisk()
    {
        var source = await TransitionAsync(RequestId, "refused", new { note = "Not licensed for reuse." });

        var permission = source.Permission.ShouldNotBeNull();
        (permission.Status, permission.Note).ShouldBe(("refused", "Not licensed for reuse."));
        permission.RespondedLabel.ShouldStartWith("Refused ");
        (source.Verdict.Status, source.Verdict.Reason).ShouldBe(("risk", "permission_refused"));
        (await GetSummaryAsync()).Risks.ShouldBe(3);
    }

    [Test, Order(2)]
    public async Task Export_WhenRefused_ShouldShowTheNoteAsANoteNotAsTerms()
    {
        using var response = await Client.GetAsync($"/api/books/{SeedIds.Books.ProductEngineering}/sources/export?format=permissions");

        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldEndWith(",Credit line,Terms,Note");
        var row = lines.Single(l => l.StartsWith("Team Topologies,", StringComparison.Ordinal));
        (row.Contains(",refused,", StringComparison.Ordinal), row.EndsWith(",,,Not licensed for reuse.", StringComparison.Ordinal)).ShouldBe((true, true), row);
    }

    [Test, Order(3)]
    public async Task Refuse_Twice_ShouldBe409()
    {
        using var response = await PostAsync(RequestId, "refused", new { note = "Again" });

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(4)]
    public async Task MarkSent_AfterARefusal_ShouldBe409()
    {
        using var response = await PostAsync(RequestId, "sent");

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(5)]
    public async Task Draft_AfterARefusal_ShouldStartANewRequest()
    {
        var source = await DraftAsync(SeedIds.References.TeamTopologies);

        source.Permission.ShouldNotBeNull().Status.ShouldBe("draft");
        _redrafted = source.Permission.Id;
        source.Permission.Letter.ShouldContain("1 figure");
    }

    [Test, Order(6)]
    public async Task Refuse_WithoutABody_ShouldBeAcceptedAsNoNote()
    {
        await TransitionAsync(_redrafted, "sent");

        using var response = await Client.PostAsync($"/api/permission-requests/{_redrafted}/refused", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        (source.Permission.ShouldNotBeNull().Status, source.Permission.Note).ShouldBe(("refused", null));
    }

    [Test, Order(7)]
    public async Task Refuse_WithMalformedJson_ShouldBe400()
    {
        using var body = new StringContent("{\"note\":", System.Text.Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync($"/api/permission-requests/{_redrafted}/refused", body);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(8)]
    public async Task Refuse_WithAPlainTextBody_ShouldBe415()
    {
        using var body = new StringContent("Not licensed", System.Text.Encoding.UTF8, "text/plain");

        using var response = await Client.PostAsync($"/api/permission-requests/{_redrafted}/refused", body);

        (await response.ReadApiErrorAsync(HttpStatusCode.UnsupportedMediaType)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
