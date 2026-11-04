using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Ai;

namespace Codex.AcceptanceTests.Journeys;

// J22 negative path: a taken template name, an empty body or one without the beat, and a model server on a local
// http:// address, which Settings → AI refuses unless the server owner allows local endpoints (#36). Needs a fresh seed.
[TestFixture, Category("J22")]
public class J22_ManagePromptTemplatesAndModels_Negative : JourneyTest
{
    [Test, Order(1)]
    public async Task Step1_WhenTheTemplateNameIsTaken_ShouldShowAnInlineErrorAndAddNoRow()
    {
        var templates = new AiTemplatesSection(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        await templates.DuplicateAsync("continue-writing", "Mine");
        await templates.EditorSave.ClickAsync();
        await Expect(templates.EditorBody).ToHaveCountAsync(0);

        await templates.DuplicateAsync("paraphrase", "mine");

        await Expect(templates.DuplicateError).ToHaveTextAsync("You already have a template with that name.");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prompt_template")).ShouldBe(1);
        await Expect(templates.TemplateRows).ToHaveCountAsync(4);
    }

    [Test, Order(2)]
    public async Task Step2_WhenTheBodyIsEmptyOrLacksTheBeat_ShouldDisableSaveOrWarn()
    {
        var templates = new AiTemplatesSection(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);
        var id = (await ScalarSqlAsync<Guid>("SELECT id FROM prompt_template")).ToString();
        await templates.TemplateEdit(id).ClickAsync();

        await templates.EditorBody.FillAsync("");

        await Expect(templates.EditorError).ToHaveTextAsync("The prompt cannot be empty");
        await Expect(templates.EditorSave).ToBeDisabledAsync();
        await templates.EditorBody.FillAsync("Write something without a placeholder.");
        await Expect(templates.EditorWarning).ToHaveTextAsync("The model will not see the beat");
        await Expect(templates.EditorSave).ToBeEnabledAsync();
        var before = await ScalarSqlAsync<string>("SELECT body FROM prompt_template");

        await templates.EditorCancel.ClickAsync();

        (await ScalarSqlAsync<string>("SELECT body FROM prompt_template")).ShouldBe(before);
        await Expect(templates.EditorBody).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Step3_WhenAModelServerIsOnALocalHttpAddress_ShouldRefuseItAndSaveNothing()
    {
        var ai = new AiSettingsScreen(Page);
        await Page.GotoAsync(AiSettingsScreen.Path);

        await ai.Connect.ClickAsync();
        await ai.Kind("openai_compatible").ClickAsync();
        await ai.Label.FillAsync("Ollama llama3");
        await ai.BaseUrl.FillAsync("http://localhost:11434/v1");
        await ai.ApiKey.FillAsync("local-key");
        await ai.TestConnection.ClickAsync();

        await Expect(ai.DialogError).ToHaveTextAsync("Use an https:// address.");
        await ai.Save.ClickAsync();
        await Expect(ai.DialogError).ToHaveTextAsync("Use an https:// address.");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM ai_integration")).ShouldBe(0);
    }
}
