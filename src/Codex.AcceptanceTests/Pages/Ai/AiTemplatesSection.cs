using System.Text.RegularExpressions;

namespace Codex.AcceptanceTests.Pages.Ai;

// /settings/ai, the Prompt templates section (J22): built-in templates beside the user's own, and the template editor.
public sealed class AiTemplatesSection(IPage page)
{
    public ILocator Root => page.GetByTestId(AiTestIds.Templates);

    public ILocator TemplateRows => page.GetByTestId(new Regex("^ai-template-row-"));

    public ILocator TemplateRow(string id) => page.GetByTestId(AiTestIds.TemplateRow(id));

    public ILocator TemplateBuiltIn(string id) => page.GetByTestId(AiTestIds.TemplateBuiltIn(id));

    public ILocator TemplateEdit(string id) => page.GetByTestId(AiTestIds.TemplateEdit(id));

    public ILocator TemplateDuplicate(string id) => page.GetByTestId(AiTestIds.TemplateDuplicate(id));

    public ILocator DuplicateError => page.GetByTestId(AiTestIds.DuplicateError);

    public ILocator EditorBody => page.GetByTestId(AiTestIds.EditorBody);

    public ILocator EditorTokens => page.GetByTestId(AiTestIds.EditorTokens);

    public ILocator EditorWarning => page.GetByTestId(AiTestIds.EditorWarning);

    public ILocator EditorError => page.GetByTestId(AiTestIds.EditorError);

    public ILocator EditorSave => page.GetByTestId(AiTestIds.EditorSave);

    public ILocator EditorCancel => page.GetByTestId(AiTestIds.EditorCancel);

    public ILocator EditorVariable(string name) => page.GetByTestId(AiTestIds.EditorVariable(name));

    public async Task DuplicateAsync(string templateId, string name)
    {
        await TemplateDuplicate(templateId).ClickAsync();
        await page.GetByTestId(AiTestIds.DuplicateName).FillAsync(name);
        await page.GetByTestId(AiTestIds.DuplicateSave).ClickAsync();
    }
}
