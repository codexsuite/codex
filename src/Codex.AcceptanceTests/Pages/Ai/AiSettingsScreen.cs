namespace Codex.AcceptanceTests.Pages.Ai;

// /settings/ai: the user's connected AI providers and the Connect dialog (#36). The Prompt templates section below them
// is AiTemplatesSection.
public sealed class AiSettingsScreen(IPage page)
{
    public const string Path = "/settings/ai";

    public ILocator Root => page.GetByTestId(AiTestIds.Page);

    public ILocator Empty => page.GetByTestId(AiTestIds.Empty);

    public ILocator Connect => page.GetByTestId(AiTestIds.Connect);

    public ILocator Row(Guid id) => page.GetByTestId(AiTestIds.Row(id));

    public ILocator List => page.GetByTestId(AiTestIds.List);

    public ILocator DefaultBadge(Guid id) => page.GetByTestId(AiTestIds.RowDefaultBadge(id));

    public ILocator KeyHint(Guid id) => page.GetByTestId(AiTestIds.RowKeyHint(id));

    public ILocator RowModel(Guid id) => page.GetByTestId(AiTestIds.RowModel(id));

    public ILocator RowTest(Guid id) => page.GetByTestId(AiTestIds.RowTest(id));

    public ILocator RowTestResult(Guid id) => page.GetByTestId(AiTestIds.RowTestResult(id));

    // Change default model on a row whose models are listed (a Test caches them): pick one in the select, then Save.
    public async Task ChangeDefaultModelAsync(Guid id, string model)
    {
        await page.GetByTestId(AiTestIds.RowChangeModel(id)).ClickAsync();
        await page.GetByTestId(AiTestIds.RowEditInput(id)).SelectOptionAsync(model);
        await page.GetByTestId(AiTestIds.RowEditSave(id)).ClickAsync();
    }

    public ILocator Dialog => page.GetByTestId(AiTestIds.Dialog);

    public ILocator Kind(string kind) => page.GetByTestId(AiTestIds.DialogKind(kind));

    public ILocator Label => page.GetByTestId(AiTestIds.DialogLabel);

    public ILocator BaseUrl => page.GetByTestId(AiTestIds.DialogBaseUrl);

    public ILocator ApiKey => page.GetByTestId(AiTestIds.DialogApiKey);

    public ILocator TestConnection => page.GetByTestId(AiTestIds.DialogTest);

    public ILocator TestResult => page.GetByTestId(AiTestIds.DialogTestResult);

    public ILocator Model => page.GetByTestId(AiTestIds.DialogModel);

    public ILocator Save => page.GetByTestId(AiTestIds.DialogSave);

    public ILocator DialogError => page.GetByTestId(AiTestIds.DialogError);
}
