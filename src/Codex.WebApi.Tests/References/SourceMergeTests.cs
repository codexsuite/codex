using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Dapper;

namespace Codex.WebApi.Tests.References;

// J24 step 5 and its negative paths: a duplicate Accelerate is folded into the real one.
[TestFixture]
public class SourceMergeTests : PermissionApiFixture
{
    private Guid _duplicate;

    private Task<HttpResponseMessage> MergeAsync(Guid source, Guid into) =>
        Client.PostAsJsonAsync($"/api/references/{source}/merge", new MergeSourceRequest(into));

    [Test, Order(1)]
    public async Task Add_WhenEnteredByHandWithoutAnIsbn_ShouldCreateADuplicateOfAccelerate()
    {
        using var response = await Client.PostAsJsonAsync(
            $"/api/books/{SeedIds.Books.ProductEngineering}/references",
            new AddSourceRequest(new SourceDetailsRequest("book", "Accelerate", "Forsgren, Nicole", null, null, "2nd", null, null, null, null, "unknown", "Dup Press"), null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _duplicate = (await response.Content.ReadFromJsonAsync<AddSourceResult>())!.Id!.Value;

        (await GetSummaryAsync()).Total.ShouldBe(13);
    }

    [Test, Order(2)]
    public async Task Merge_IntoItself_ShouldBe400()
    {
        using var response = await MergeAsync(_duplicate, _duplicate);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(3)]
    public async Task Merge_WhenBothHaveALivePermissionRequest_ShouldBeBlocked()
    {
        await DraftAsync(_duplicate);
        await DraftAsync(SeedIds.References.Accelerate);

        using var response = await MergeAsync(_duplicate, SeedIds.References.Accelerate);

        var error = await response.ReadApiErrorAsync((HttpStatusCode)422);
        error.Code.ShouldBe(ApiErrorCodes.Blocked);
        error.Error.ShouldBe("Both have a permission request. Withdraw one first.");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM reference WHERE id = @id", new { id = _duplicate })).ShouldBeTrue();
        await db.ExecuteAsync("DELETE FROM permission_request WHERE reference_id = @id", new { id = _duplicate });
        await db.ExecuteAsync("DELETE FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.Accelerate });
    }

    [Test, Order(4)]
    public async Task Merge_ShouldKeepTheTargetsDetailsFillItsGapsAndArchiveTheDuplicate()
    {
        var before = await GetSourceAsync(SeedIds.References.Accelerate);
        var usesBefore = before.Uses.Count;

        using var response = await MergeAsync(_duplicate, SeedIds.References.Accelerate);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        target.Id.ShouldBe(SeedIds.References.Accelerate);
        target.Title.ShouldBe(before.Title);
        target.Uses.Count.ShouldBe(usesBefore);
        target.History[0].Text.ShouldStartWith("merged Accelerate into this source");
        (await GetSummaryAsync()).Total.ShouldBe(12);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid?>("SELECT merged_into_id FROM reference WHERE id = @id", new { id = _duplicate })).ShouldBe(SeedIds.References.Accelerate);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @id", new { id = _duplicate })).ShouldBeTrue();
        (await Client.GetAsync($"/api/references/{_duplicate}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(5)]
    public async Task Merge_WhenTheDuplicateHasUses_ShouldMoveThemAndReopenATargetSignOff()
    {
        var cagan = await GetSourceAsync(SeedIds.References.CaganEmpowered);
        cagan.Verdict.Status.ShouldBe("cleared");

        using var response = await MergeAsync(SeedIds.References.DoraReport2024, SeedIds.References.CaganEmpowered);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        target.Uses.Count.ShouldBe(cagan.Uses.Count + 1);
        (target.Verdict.Status, target.Verdict.Reason).ShouldBe(("needs_check", "new_uses_since_signoff"));
    }
}
