using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Core;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J10 · Clear a copyright risk (#16 Part 2), path 1 (paraphrase, sign off), keyboard only (#16 review section D): the
// same path as J10_ClearACopyrightRisk with Tab, Enter, Space and the arrow keys and no pointer after the start screen.
[TestFixture, Category("J10")]
public class J10_ClearACopyrightRisk_Keyboard : JourneyTest
{
    private const string Rewrite = "Velocity became the number everyone watched and nobody understood. ";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Velocity = SeedIds.References.VelocityTrap.ToString();
    private static readonly Guid Ch4Marker = SeedIds.ReferenceUses.VelocityTrapCh4Sc1;
    private static readonly Guid Ch8Use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
    private static readonly Guid Ch8Scene = SeedIds.Scenes.Ch8Sc1;
    private static readonly FakeProseGenerator Generator = new([Rewrite]);

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private static string ReferencesUrl => Links.References(BookId, Links.Hash((Velocity, null)));

    [Test, Order(1)]
    public async Task Step1_WhenMarker4OpenedByEnter_ShouldShowCardAndOpenInReferencesByEnter()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Markers).ToHaveCountAsync(15);

        await Page.ActivateAsync(write.Marker(Ch4Marker));

        await Expect(write.MarkerCardSource).ToContainTextAsync("The Velocity Trap");
        await Expect(write.MarkerCardStatus).ToHaveTextAsync("Copyright risk");
        await Expect(write.MarkerCardWords).ToHaveTextAsync("342 of 300 quoted words");
        await Page.ActivateAsync(write.MarkerCardOpen);
        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/references#{Velocity}$"));
    }

    [Test, Order(2)]
    public async Task Step2_WhenCh8QuoteParaphrasedWithAiByKeyboard_ShouldGenerateApplyAndSwitchTheKind()
    {
        var panel = new SourcePanel(Page);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(ReferencesUrl);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");

        await Page.ActivateAsync(new CiteHandOffPage(Page).Paraphrase(Ch8Use));

        await Expect(Page).ToHaveURLAsync(BaseUrl + Links.Write(BookId, Links.Hash(("scene", "8.1"), ("ref", Velocity))));
        await Expect(write.BeatCards(Ch8Scene)).ToHaveCountAsync(2);
        var beatId = await ScalarSqlAsync<Guid>(
            "SELECT id FROM beat WHERE scene_id = @scene AND body LIKE 'Paraphrase the passage quoted from The Velocity Trap%'", ("scene", Ch8Scene));
        var generated = await Page.RunAndWaitForApiAsync($"/api/beats/{beatId}/generate", "POST", () => Page.ActivateAsync(write.BeatGenerate(beatId)));
        generated.Ok.ShouldBeTrue();
        await Expect(write.Draft(beatId)).ToHaveAttributeAsync("data-state", "pending");
        var generationId = await write.Draft(beatId).GetAttributeAsync("data-generation-id");
        var applied = await Page.RunAndWaitForApiAsync($"/api/generations/{generationId}/apply", "POST", () => Page.ActivateAsync(write.DraftApply(beatId)));
        applied.Ok.ShouldBeTrue();
        await Expect(write.ProseEditor(Ch8Scene)).ToContainTextAsync(Rewrite.Trim());

        await Page.GotoAsync(ReferencesUrl);
        await Expect(panel.UseKind(Ch8Use)).ToHaveValueAsync("quote");
        await Page.TabToAsync(panel.UseKind(Ch8Use));
        var patched = await Page.RunAndWaitForApiAsync($"/api/reference-uses/{Ch8Use}", "PATCH", () => Page.Keyboard.PressAsync("ArrowDown"));

        patched.Ok.ShouldBeTrue();
        await Expect(panel.UseKind(Ch8Use)).ToHaveValueAsync("paraphrase");
    }

    [Test, Order(3)]
    public async Task Step3_WhenSignOffPressedByKeyboard_ShouldReadCleared()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Velocity}/sign-off", "POST", () => Page.ActivateAsync(panel.VerdictAction));

        response.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(panel.SignedOff).ToHaveTextAsync($"Signed off by Joel Dickson · {ExpectedLabels.JustNow()}");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");
    }
}
