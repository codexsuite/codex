using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Ai;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Application.Features.Ai.Providers;
using Codex.Application.Network;
using Codex.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.Ai;

// Settings → AI → Connect → OpenAI-compatible, Test connection lists the gateway's models, Save, then a beat drafted in
// Write streams the gateway's text. The gateway is a stubbed primary handler on the named HttpClient.
[TestFixture]
public class ConnectOpenAiCompatibleTests : SeededAcceptanceTest
{
    private const string GatewayUrl = "https://gateway.test/v1";
    private const string Key = "sk-gateway-secret-1234";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Beat = SeedIds.Beats.Ch3Sc3BadScrum;
    private static readonly StubGateway Gateway = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddSingleton<IHostResolver, PublicHostResolver>();
        services.AddHttpClient(AiHttpClients.OpenAiCompatible).ConfigurePrimaryHttpMessageHandler(() => Gateway);
    };

    [Test, Order(1)]
    public async Task Connect_WhenTestedAndSaved_ShouldListTheGatewayWithAKeyHintOnly()
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
        await Expect(ai.List).ToContainTextAsync(GatewayUrl);
        await Expect(ai.List).Not.ToContainTextAsync(Key);
    }

    [Test, Order(2)]
    public async Task Draft_WhenBeatGenerated_ShouldStreamTheGatewaysText()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.BeatGenerate(Beat)).ToBeVisibleAsync();
        await Expect(write.BeatChoice(Beat)).ToContainTextAsync(StubGateway.FirstModel);

        await Page.RunAndWaitForApiAsync($"/api/beats/{Beat}/generate", "POST", () => write.BeatGenerate(Beat).ClickAsync());

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(Beat)).ToContainTextAsync(StubGateway.Text);
        Gateway.ChatBodies.ShouldContain(body => body.Contains(StubGateway.FirstModel, StringComparison.Ordinal));
    }
}
