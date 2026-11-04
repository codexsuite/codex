using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.Contracts.Storage;
using Codex.Core;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J10 · Clear a copyright risk (#16 Part 2), path 2 "ask permission", keyboard only (#16 review section D): the
// References tab and row, Draft request and Mark sent of J10_ClearACopyrightRisk_AskPermission with Tab, Enter and
// typing, and no pointer after the start screen.
[TestFixture, Category("J10")]
public class J10_ClearACopyrightRisk_AskPermission_Keyboard : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Velocity = SeedIds.References.VelocityTrap.ToString();

    private static Guid _permissionId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(new InMemoryBlobStore());

    private static string ReferencesUrl => Links.References(BookId, Links.Hash((Velocity, null)));

    private static string Today()
    {
        var now = TimeProvider.System.GetUtcNow();
        return DisplayDate.ShortDay(now, now, TimeProvider.System.LocalTimeZone);
    }

    [Test, Order(1)]
    public async Task Step1_WhenOpenedFromTheReferencesTabAndRowByKeyboard_ShouldShowCopyrightRiskOn342Of300Words()
    {
        var bar = new AppShell(Page).BookBar;
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");

        await Page.ActivateAsync(bar.TabReferences);
        await Page.ActivateAsync(new ReferencesScreen(Page).Row(SeedIds.References.VelocityTrap));

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 342 of 300 words");
    }

    [Test, Order(2)]
    public async Task Step2_WhenDraftRequestPressedByKeyboard_ShouldWriteTheLetterFromTheUses()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Velocity}/permission", "POST", () => Page.ActivateAsync(panel.PermDraft));

        response.Ok.ShouldBeTrue();
        await Expect(panel.PermLetter).ToHaveValueAsync(new Regex("342 words of quoted prose"));
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission drafted");
        _permissionId = await ScalarSqlAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", ("id", SeedIds.References.VelocityTrap));
    }

    [Test, Order(3)]
    public async Task Step3_WhenLetterTypedAndMarkedSentByKeyboard_ShouldStayCopyrightRiskWithPermissionAsked()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);
        await Page.TabToAsync(panel.PermTo);
        await Page.Keyboard.TypeAsync("permissions@velocity.example");
        await Page.TabToAsync(panel.PermLetter);
        await Page.Keyboard.PressAsync("Control+A");
        await Page.Keyboard.TypeAsync("Dear Ms Reinertsen, may I quote 342 words of The Velocity Trap?");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{_permissionId}/sent", "POST", () => Page.ActivateAsync(panel.PermSend));

        response.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.PermSentLabel).ToHaveTextAsync($"Asked {Today()} · no reply");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission asked, no reply");
    }
}
