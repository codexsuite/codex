using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Lanes.Ai;
using Codex.AcceptanceTests.Pages.Ai;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Application.Features.Ai.Providers;
using Codex.Application.Network;
using Codex.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J22 · Manage prompt templates and models (#16 Part 2, L18.1, reconciled with #36): on Settings → AI, write my own
// template from a built-in, connect an OpenAI-compatible gateway and choose its default model, then generate on Write
// with the new template and see what was sent. The gateway is StubGateway behind the named HttpClient: it lists two
// models and streams one sentence, reporting 12 input and 6 output tokens.
[TestFixture, Category("J22")]
public class J22_ManagePromptTemplatesAndModels : JourneyTest
{
    private const string BuiltInGeneral = "continue-writing";
    private const string MyTemplate = "Joel's voice (nonfiction)";
    private const string GatewayUrl = "https://gateway.test/v1";
    private const string Key = "sk-gateway-secret-a1b2c3d4";
    private const string SecondModel = "gpt-test-mini";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Beat = SeedIds.Beats.Ch3Sc3BadScrum;
    private static readonly StubGateway Gateway = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddSingleton<IHostResolver, PublicHostResolver>();
        services.AddHttpClient(AiHttpClients.OpenAiCompatible).ConfigurePrimaryHttpMessageHandler(() => Gateway);
    };

    private async Task<string> TemplateIdAsync() => (await ScalarSqlAsync<Guid>("SELECT id FROM prompt_template WHERE name = @name", ("name", MyTemplate))).ToString();

    private Task<Guid> IntegrationIdAsync() => ScalarSqlAsync<Guid>("SELECT id FROM ai_integration WHERE label = 'Gateway'");

    [Test, Order(1)]
    public async Task Step1_OnSettingsAi_ShouldListThreeBuiltInTemplatesWithNoEditControl()
    {
        var templates = new AiTemplatesSection(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);

        await Expect(templates.TemplateRows).ToHaveCountAsync(3);
        foreach (var id in new[] { "continue-writing", "joels-voice", "paraphrase" })
        {
            await Expect(templates.TemplateBuiltIn(id)).ToHaveTextAsync("Built in");
            await Expect(templates.TemplateEdit(id)).ToHaveCountAsync(0);
        }
    }

    [Test, Order(2)]
    public async Task Step2_WhenABuiltInIsDuplicated_ShouldOpenTheBodyWithVariablesAndATokenEstimate()
    {
        var templates = new AiTemplatesSection(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        await Expect(templates.TemplateRows).ToHaveCountAsync(3);

        await templates.DuplicateAsync(BuiltInGeneral, MyTemplate);

        await Expect(templates.TemplateRows).ToHaveCountAsync(4);
        await Expect(templates.EditorBody).ToContainTextAsync("{{beat}}");
        foreach (var variable in new[] { "beat", "context", "style_guide", "previous_prose", "length" })
        {
            await Expect(templates.EditorVariable(variable)).ToBeVisibleAsync();
        }

        await Expect(templates.EditorTokens).ToContainTextAsync("tokens");
        await templates.EditorSave.ClickAsync();
        await Expect(templates.EditorBody).ToHaveCountAsync(0);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prompt_template")).ShouldBe(1);
    }

    [Test, Order(3)]
    public async Task Step3_WhenMyTemplateIsEdited_ShouldStoreTheNewBody()
    {
        var templates = new AiTemplatesSection(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        var id = await TemplateIdAsync();

        await templates.TemplateEdit(id).ClickAsync();
        await templates.EditorBody.FillAsync("Be brisk and concrete.\nBeat: {{beat}}\nAbout {{length}} words.");
        await templates.EditorSave.ClickAsync();

        await Expect(templates.EditorBody).ToHaveCountAsync(0);
        (await ScalarSqlAsync<string>("SELECT body FROM prompt_template WHERE id = @id::uuid", ("id", id))).ShouldStartWith("Be brisk and concrete.");
    }

    [Test, Order(4)]
    public async Task Step4_WhenAGatewayIsConnected_ShouldShowOnlyTheKeyHintAndOfferMyTemplateOnWrite()
    {
        var ai = new AiSettingsScreen(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        await Expect(ai.Empty).ToBeVisibleAsync();

        await ai.Connect.ClickAsync();
        await ai.Kind("openai_compatible").ClickAsync();
        await ai.Label.FillAsync("Gateway");
        await ai.BaseUrl.FillAsync(GatewayUrl);
        await ai.ApiKey.FillAsync(Key);
        await ai.TestConnection.ClickAsync();
        await Expect(ai.TestResult).ToContainTextAsync("2 models");
        await ai.Model.SelectOptionAsync(StubGateway.FirstModel);
        await Page.RunAndWaitForApiAsync("/api/ai/integrations", "POST", () => ai.Save.ClickAsync());

        await Expect(ai.Dialog).ToHaveCountAsync(0);
        await Expect(ai.List).ToContainTextAsync("Gateway");
        await Expect(ai.DefaultBadge(await IntegrationIdAsync())).ToBeVisibleAsync();
        (await Page.ContentAsync()).ShouldNotContain(Key);
        (await ScalarSqlAsync<string>("SELECT credential FROM ai_integration")).ShouldNotBeNull().ShouldNotContain("a1b2c3d4");
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatChoice(Beat)).ToContainTextAsync(StubGateway.FirstModel);
        await write.BeatChoice(Beat).ClickAsync();
        await Expect(Page.GetByTestId(new Regex("^write-draft-template-"))).ToHaveCountAsync(4);
        await Expect(write.DraftTemplate(await TemplateIdAsync())).ToHaveTextAsync(MyTemplate);
    }

    [Test, Order(5)]
    public async Task Step5_WhenTheGatewaysDefaultModelIsChanged_ShouldShowItTickedOnWrite()
    {
        var ai = new AiSettingsScreen(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        var integration = await IntegrationIdAsync();
        await ai.RowTest(integration).ClickAsync();
        await Expect(ai.RowTestResult(integration)).ToContainTextAsync("2 models");

        await ai.ChangeDefaultModelAsync(integration, SecondModel);

        await Expect(ai.RowModel(integration)).ToHaveTextAsync(SecondModel);
        (await ScalarSqlAsync<string>("SELECT default_model FROM ai_integration WHERE id = @integration", ("integration", integration))).ShouldBe(SecondModel);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await write.BeatChoice(Beat).ClickAsync();
        await Expect(write.DraftModel(integration, SecondModel)).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(write.DraftModel(integration, StubGateway.FirstModel)).ToHaveAttributeAsync("aria-checked", "false");
    }

    [Test, Order(6)]
    public async Task Step6_WhenGeneratingWithMyTemplate_ShouldSendItThroughTheGatewayAndShowWhatWasSent()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await write.BeatChoice(Beat).ClickAsync();
        await write.DraftTemplate(await TemplateIdAsync()).ClickAsync();
        await write.DraftModel(await IntegrationIdAsync(), SecondModel).ClickAsync();
        await Expect(write.BeatChoice(Beat)).ToHaveTextAsync($"{MyTemplate} · {SecondModel}");

        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", () => write.BeatGenerate(Beat).ClickAsync());

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToContainTextAsync(StubGateway.Text);
        (await ScalarSqlAsync<string>("SELECT model FROM ai_generation WHERE beat_id = @beat ORDER BY created_at DESC LIMIT 1", ("beat", Beat))).ShouldBe(SecondModel);
        Gateway.ChatBodies[^1].ShouldContain("Be brisk and concrete.");
        Gateway.ChatBodies[^1].ShouldContain(SecondModel);

        await write.DraftSent(Beat).ClickAsync();

        var popover = write.DraftSentPopover(Beat);
        await Expect(popover).ToContainTextAsync(MyTemplate);
        await Expect(popover).ToContainTextAsync($"{SecondModel} (Gateway)");
        await Expect(popover).ToContainTextAsync("12 in, 6 out");
        await Expect(popover).ToContainTextAsync("Be brisk and concrete.");
        await Expect(popover).Not.ToContainTextAsync(Key);
    }
}
