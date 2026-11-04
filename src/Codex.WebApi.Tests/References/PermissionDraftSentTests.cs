using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Core;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// The Velocity Trap story: draft from the uses, edit, remind, send, then withdraw and start over.
[TestFixture]
public class PermissionDraftSentTests : PermissionApiFixture
{
    private Guid _requestId;

    [Test, Order(1)]
    public async Task Draft_ShouldWriteTheLetterFromTheUses()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);
        before.Permission.ShouldBeNull();

        var source = await DraftAsync(SeedIds.References.VelocityTrap);

        var permission = source.Permission.ShouldNotBeNull();
        _requestId = permission.Id;
        permission.Status.ShouldBe("draft");
        permission.Letter.ShouldContain("342 words of quoted prose");
        permission.Letter.ShouldContain("pp. 41-43 and p. 118");
        permission.Letter.ShouldContain("Chapters 4 and 8");
        permission.SentLabel.ShouldBeNull();
        (source.Verdict.Status, source.Verdict.Reason).ShouldBe(("risk", "permission_drafted"));
        source.History[0].Text.ShouldBe("drafted a permission request");
    }

    [Test, Order(2)]
    public async Task Draft_WhenOneIsLive_ShouldBe409()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.VelocityTrap}/permission", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(3)]
    public async Task Patch_ShouldEditTheLetterAndSetAReminder()
    {
        var day = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(5);
        using var response = await Client.PatchAsJsonAsync($"/api/permission-requests/{_requestId}",
            new PatchPermissionRequest("Dear Ms Reinertsen,\n\nMay I quote 342 words?", "rights@example.test", day.ToString("yyyy-MM-dd")));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var permission = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission.ShouldNotBeNull();
        (permission.Letter, permission.To).ShouldBe(("Dear Ms Reinertsen,\n\nMay I quote 342 words?", "rights@example.test"));
        permission.RemindOn.ShouldBe(day.ToString("yyyy-MM-dd"));
        permission.RemindDue.ShouldBeFalse();
    }

    [Test, Order(4)]
    public async Task Patch_WhenLetterIsBlankOrDateIsBad_ShouldBe400()
    {
        using var blank = await Client.PatchAsJsonAsync($"/api/permission-requests/{_requestId}", new PatchPermissionRequest("  ", null, null));
        using var badDate = await Client.PatchAsJsonAsync($"/api/permission-requests/{_requestId}", new PatchPermissionRequest(null, null, "next week"));

        (await blank.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await badDate.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task MarkSent_ShouldStampTheDateAndKeepTheRisk()
    {
        var source = await TransitionAsync(_requestId, "sent");

        var permission = source.Permission.ShouldNotBeNull();
        permission.Status.ShouldBe("sent");
        permission.SentLabel.ShouldStartWith("Asked ");
        permission.RemindOn.ShouldNotBeNull();
        (source.Verdict.Status, source.Verdict.Reason).ShouldBe(("risk", "permission_sent"));
    }

    [Test, Order(6)]
    public async Task MarkSent_Twice_ShouldBe409()
    {
        using var response = await PostAsync(_requestId, "sent");

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(7)]
    public async Task Patch_WhenSent_ShouldKeepTheWordingButAllowTheReminder()
    {
        using var edit = await Client.PatchAsJsonAsync($"/api/permission-requests/{_requestId}", new PatchPermissionRequest("New wording", null, null));
        using var remind = await Client.PatchAsJsonAsync($"/api/permission-requests/{_requestId}", new PatchPermissionRequest(null, null, ""));

        (await edit.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        remind.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await remind.Content.ReadFromJsonAsync<SourceDetailDto>())!.Permission!.RemindOn.ShouldBeNull();
    }

    [Test, Order(8)]
    public async Task Withdraw_ShouldReturnToNoRequest()
    {
        var source = await TransitionAsync(_requestId, "withdraw");

        source.Permission.ShouldBeNull();
        (source.Verdict.Status, source.Verdict.Reason).ShouldBe(("risk", "quote_over_limit"));
    }

    [Test, Order(9)]
    public async Task Withdraw_WhenAlreadyWithdrawn_ShouldBe409()
    {
        using var response = await PostAsync(_requestId, "withdraw");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test, Order(10)]
    public async Task Draft_AfterWithdraw_ShouldStartANewRequest()
    {
        var source = await DraftAsync(SeedIds.References.VelocityTrap);

        source.Permission.ShouldNotBeNull().Id.ShouldNotBe(_requestId);
    }

    [Test, Order(11)]
    public async Task UnknownRequest_ShouldBe404()
    {
        using var response = await PostAsync(Guid.NewGuid(), "sent");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
