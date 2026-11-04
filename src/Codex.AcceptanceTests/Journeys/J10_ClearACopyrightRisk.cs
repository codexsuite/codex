using System.Net.Http.Json;
using System.Text.Json;
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

// J10 · Clear a copyright risk (#16 Part 2), path 1: The Velocity Trap (342 of 300 quoted words) is reached from its
// red marker in Write, its Ch 8 quote is paraphrased with the AI (Scene beat, Generate, Apply, chip to Paraphrase),
// and the source drops to Needs check and is signed off. Path 2 (ask permission) is J10_ClearACopyrightRisk_AskPermission.
[TestFixture, Category("J10")]
public class J10_ClearACopyrightRisk : JourneyTest
{
    private const string Rewrite = "Velocity became the number everyone watched and nobody understood. ";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Velocity = SeedIds.References.VelocityTrap.ToString();
    private static readonly Guid Ch8Use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
    private static readonly Guid Ch8Scene = SeedIds.Scenes.Ch8Sc1;
    private static readonly FakeProseGenerator Generator = new([Rewrite]);

    private static Guid _beatId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private static string ReferencesUrl => Links.References(BookId, Links.Hash((Velocity, null)));

    private async Task<long> QuotedWordsAsync() =>
        await ScalarSqlAsync<long>(
            "SELECT COALESCE(sum(word_count), 0) FROM reference_use WHERE reference_id = @id AND kind = 'quote'",
            ("id", SeedIds.References.VelocityTrap));

    [Test, Order(1)]
    public async Task Step1_WhenDangerMarker4ClickedInCh4Sc1_ShouldShowCardAndOpenInReferences()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Markers).ToHaveCountAsync(15);

        await write.Marker(SeedIds.ReferenceUses.VelocityTrapCh4Sc1).ClickAsync();

        await Expect(write.Marker(SeedIds.ReferenceUses.VelocityTrapCh4Sc1)).ToHaveAttributeAsync("data-number", "4");
        await Expect(write.Marker(SeedIds.ReferenceUses.VelocityTrapCh4Sc1)).ToHaveAttributeAsync("data-tone", "danger");
        await Expect(write.MarkerCardSource).ToContainTextAsync("The Velocity Trap");
        await Expect(write.MarkerCardStatus).ToHaveTextAsync("Copyright risk");
        await Expect(write.MarkerCardWords).ToHaveTextAsync("342 of 300 quoted words");
        await write.MarkerCardOpen.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/references#{Velocity}$"));
    }

    [Test, Order(2)]
    public async Task Step2_WhenReferencesOpenedOnVelocity_ShouldShowCopyrightRiskVerdict()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);

        await Expect(panel.Title).ToHaveTextAsync("The Velocity Trap");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 342 of 300 words");
        // The "one next action" is the permission request (Draft request in the tracker below); Paraphrase with AI sits on the use.
        await Expect(panel.PermDraft).ToBeVisibleAsync();
        await Expect(new CiteHandOffPage(Page).Paraphrase(Ch8Use)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenCh8QuoteParaphrasedWithAi_ShouldGenerateApplyAndSwitchTheChip()
    {
        var panel = new SourcePanel(Page);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(ReferencesUrl);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");

        await new CiteHandOffPage(Page).Paraphrase(Ch8Use).ClickAsync();

        await Expect(Page).ToHaveURLAsync(BaseUrl + Links.Write(BookId, Links.Hash(("scene", "8.1"), ("ref", Velocity))));
        await Expect(write.BeatCards(Ch8Scene)).ToHaveCountAsync(2);
        _beatId = (await ScalarSqlAsync<Guid>(
            "SELECT id FROM beat WHERE scene_id = @scene AND body LIKE 'Paraphrase the passage quoted from The Velocity Trap%'", ("scene", Ch8Scene)));

        var generated = await Page.RunAndWaitForApiAsync($"/api/beats/{_beatId}/generate", "POST", () => write.BeatGenerate(_beatId).ClickAsync());
        generated.Ok.ShouldBeTrue();
        await Expect(write.Draft(_beatId)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(_beatId)).ToContainTextAsync("Velocity became the number");
        var generationId = await write.Draft(_beatId).GetAttributeAsync("data-generation-id");
        var applied = await Page.RunAndWaitForApiAsync($"/api/generations/{generationId}/apply", "POST", () => write.DraftApply(_beatId).ClickAsync());
        applied.Ok.ShouldBeTrue();
        await Expect(write.ProseEditor(Ch8Scene)).ToContainTextAsync(Rewrite.Trim());

        await Page.GotoAsync(ReferencesUrl);
        await Expect(panel.UseKind(Ch8Use)).ToHaveValueAsync("quote");
        var patched = await Page.RunAndWaitForApiAsync($"/api/reference-uses/{Ch8Use}", "PATCH", () => panel.UseKind(Ch8Use).SelectOptionAsync("paraphrase"));

        patched.Ok.ShouldBeTrue();
        await Expect(panel.UseKind(Ch8Use)).ToHaveValueAsync("paraphrase");
    }

    [Test, Order(4)]
    public async Task Step4_WhenBackInReferences_ShouldShow214QuotedWordsAndReadyToSignOff()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        (await QuotedWordsAsync()).ShouldBe(214);
        await Expect(panel.UseKind(Ch8Use)).ToHaveValueAsync("paraphrase");
    }

    [Test, Order(5)]
    public async Task Step5_WhenSignedOff_ShouldReadClearedWithInitialsAndDay()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);
        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Velocity}/sign-off", "POST", () => panel.VerdictAction.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        // Deviation: the panel spells the sign-off "Signed off by Joel Dickson · just now" (relative, then the day), not "JD, 19 Oct".
        await Expect(panel.SignedOff).ToHaveTextAsync($"Signed off by Joel Dickson · {ExpectedLabels.JustNow()}");
        (await ScalarSqlAsync<DateTime?>("SELECT signed_off_at FROM reference WHERE id = @id", ("id", SeedIds.References.VelocityTrap))).ShouldNotBeNull();
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");
    }
}
