using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Core;

namespace Codex.AcceptanceTests.Lanes.References;

// The Velocity Trap (342 of 300 words): draft the request from its uses, edit it, mark it sent, then withdraw it.
[TestFixture]
public class PermissionDraftSentTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.VelocityTrap;

    private static string Today() => DisplayDate.ShortDay(TimeProvider.System.GetUtcNow(), TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone);

    [Test, Order(1)]
    public async Task Draft_WhenClicked_ShouldWriteTheLetterFromTheUses()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/permission", "POST", () => panel.PermDraft.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermLetter).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("342 words of quoted prose"));
        await Expect(panel.PermLetter).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("Chapters 4 and 8"));
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission drafted");
    }

    [Test, Order(2)]
    public async Task Draft_ThenSent_ShouldShowSent()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        await panel.PermTo.FillAsync("permissions@velocity.example");
        await panel.PermLetter.FillAsync("Dear Ms Reinertsen,\n\nMay I quote 342 words of The Velocity Trap?");

        var response = await Page.RunAndWaitForApiAsync("/api/permission-requests/" + await PermissionIdAsync() + "/sent", "POST", () => panel.PermSend.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermSentLabel).ToHaveTextAsync($"Asked {Today()} · no reply");
        await Expect(panel.PermRemindLabel).ToContainTextAsync("Reminder ");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission asked, no reply");
        await Page.ReloadAsync();
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission asked");
    }

    [Test, Order(3)]
    public async Task MarkSent_WhenAlreadySent_ShouldBeRefusedWith409()
    {
        var response = await Page.APIRequest.PostAsync($"{BaseUrl}/api/permission-requests/{await PermissionIdAsync()}/sent");

        response.Status.ShouldBe(409);
    }

    [Test, Order(4)]
    public async Task Withdraw_ShouldReturnToNoRequest()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        var response = await Page.RunAndWaitForApiAsync("/api/permission-requests/" + await PermissionIdAsync() + "/withdraw", "POST", () => panel.PermWithdraw.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermDraft).ToBeVisibleAsync();
        await Expect(panel.PermStatus).ToHaveCountAsync(0);
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 342 of 300 words");
    }

    private async Task<Guid> PermissionIdAsync()
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/references/{Source}");
        return (await response.JsonAsync())!.Value.GetProperty("permission").GetProperty("id").GetGuid();
    }
}
