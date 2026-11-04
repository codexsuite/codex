namespace Codex.AcceptanceTests.Pages.References;

// The toolbar's Add source popover and Import dialog.
public sealed class AddSourceForm(IPage page)
{
    public ILocator Open => page.GetByTestId(ReferencesTestIds.AddOpen);

    public ILocator Form => page.GetByTestId(ReferencesTestIds.AddForm);

    public ILocator Input => page.GetByTestId(ReferencesTestIds.AddInput);

    public ILocator Error => page.GetByTestId(ReferencesTestIds.AddError);

    public ILocator Match => page.GetByTestId(ReferencesTestIds.AddMatch);

    public ILocator MatchTitle => page.GetByTestId(ReferencesTestIds.AddMatchTitle);

    public ILocator NotFound => page.GetByTestId(ReferencesTestIds.AddNotFound);

    public ILocator Lookup => page.GetByTestId(ReferencesTestIds.AddLookup);

    public ILocator UseExisting => page.GetByTestId(ReferencesTestIds.AddUseExisting);

    public ILocator Manual => page.GetByTestId(ReferencesTestIds.AddManual);

    public ILocator ManualForm => page.GetByTestId(ReferencesTestIds.AddManualForm);

    public ILocator Save => page.GetByTestId(ReferencesTestIds.AddSave);

    public ILocator Field(string name) => page.GetByTestId(ReferencesTestIds.AddField(name));

    public ILocator ImportOpen => page.GetByTestId(ReferencesTestIds.AddImportOpen);

    public ILocator ImportFile => page.GetByTestId(ReferencesTestIds.AddImportFile);

    public ILocator ImportSummary => page.GetByTestId(ReferencesTestIds.AddImportSummary);

    public ILocator ImportProblems => page.GetByTestId(ReferencesTestIds.AddImportProblem);

    public ILocator ImportSkipped => page.GetByTestId(ReferencesTestIds.AddImportSkipped);

    public ILocator ImportSubmit => page.GetByTestId(ReferencesTestIds.AddImportSubmit);

    public ILocator ImportResult => page.GetByTestId(ReferencesTestIds.AddImportResult);
}
