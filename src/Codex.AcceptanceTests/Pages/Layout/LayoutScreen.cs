namespace Codex.AcceptanceTests.Pages.Layout;

// The Layout screen: toolbar, page canvas, status bar and the panel.
public sealed class LayoutScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(LayoutTestIds.Page);

    public ILocator ScopeSelect => page.GetByTestId(LayoutTestIds.ScopeSelect);

    public ILocator TemplateSelect => page.GetByTestId(LayoutTestIds.TemplateSelect);

    public ILocator TrimSelect => page.GetByTestId(LayoutTestIds.TrimSelect);

    public ILocator IssuesToggle => page.GetByTestId(LayoutTestIds.IssuesToggle);

    public ILocator Canvas => page.GetByTestId(LayoutTestIds.Canvas);

    public ILocator StatusPages => page.GetByTestId(LayoutTestIds.StatusPages);

    public ILocator Rendering => page.GetByTestId(LayoutTestIds.StatusRendering);

    public ILocator RailPage(int n) => page.GetByTestId(LayoutTestIds.RailPage(n));

    public ILocator DpiFlags => page.GetByTestId(LayoutTestIds.DpiFlag);

    public ILocator LatexMarks => page.GetByTestId(LayoutTestIds.LatexMark);

    public ILocator BlockCard => page.GetByTestId(LayoutTestIds.BlockCard);

    public ILocator OpenInWrite => page.GetByTestId(LayoutTestIds.OpenInWrite);

    public ILocator HouseBanner => page.GetByTestId(LayoutTestIds.HouseBanner);

    public ILocator Tab(string name) => page.GetByTestId(LayoutTestIds.Tab(name));

    public ILocator IssueItems => page.GetByTestId(LayoutTestIds.IssueItem);

    public ILocator Option(string key) => page.GetByTestId(LayoutTestIds.Option(key));

    public ILocator ErrorState => page.GetByTestId(LayoutTestIds.PreviewError);

    public ILocator ModeQuick => page.GetByTestId(LayoutTestIds.ModeQuick);

    public ILocator ModeProof => page.GetByTestId(LayoutTestIds.ModeProof);

    public ILocator ProofView => page.GetByTestId(LayoutTestIds.ProofView);

    public ILocator ProofBanner => page.GetByTestId(LayoutTestIds.ProofBanner);

    public ILocator ProofSteps => page.GetByTestId(LayoutTestIds.ProofSteps);

    public ILocator ProofPages => page.GetByTestId(LayoutTestIds.ProofPages);

    public ILocator ProofPage(int n) => page.GetByTestId(LayoutTestIds.ProofPage(n));

    public ILocator ProofCached => page.GetByTestId(LayoutTestIds.ProofCached);

    public ILocator ProofOutOfDate => page.GetByTestId(LayoutTestIds.ProofOutOfDate);

    public ILocator ProofDownload => page.GetByTestId(LayoutTestIds.ProofDownload);

    public ILocator ProofLogToggle => page.GetByTestId(LayoutTestIds.ProofLogToggle);

    public ILocator ProofLog => page.GetByTestId(LayoutTestIds.ProofLog);

    public ILocator ProofRerender => page.GetByTestId(LayoutTestIds.ProofRerender);

    public ILocator ProofError => page.GetByTestId(LayoutTestIds.ProofError);

    public ILocator ExportOpen => page.GetByTestId(LayoutTestIds.ExportOpen);

    public ILocator ExportDialog => page.GetByTestId(LayoutTestIds.ExportDialog);

    public ILocator ExportBleed(string value) => page.GetByTestId(LayoutTestIds.ExportBleed(value));

    public ILocator ExportCrop => page.GetByTestId(LayoutTestIds.ExportCrop);

    public ILocator ExportQuality(string value) => page.GetByTestId(LayoutTestIds.ExportQuality(value));

    public ILocator ExportPreflight => page.GetByTestId(LayoutTestIds.ExportPreflight);

    public ILocator ExportVerdict => page.GetByTestId(LayoutTestIds.ExportVerdict);

    public ILocator ExportDraft => page.GetByTestId(LayoutTestIds.ExportDraft);

    public ILocator ExportQueue => page.GetByTestId(LayoutTestIds.ExportQueue);

    public ILocator ExportCancelDialog => page.GetByTestId(LayoutTestIds.ExportCancelDialog);

    public ILocator ExportError => page.GetByTestId(LayoutTestIds.ExportError);

    public ILocator PreflightItems => page.GetByTestId(LayoutTestIds.PreflightItem);

    public ILocator ExportsList => page.GetByTestId(LayoutTestIds.ExportsList);

    public ILocator ExportRunning => page.GetByTestId(LayoutTestIds.ExportRunning);

    public ILocator ExportSteps => page.GetByTestId(LayoutTestIds.ExportSteps);

    public ILocator ExportCancel => page.GetByTestId(LayoutTestIds.ExportCancel);

    public ILocator ExportItems => page.GetByTestId(LayoutTestIds.ExportItem);

    public ILocator SourceItems => page.GetByTestId(LayoutTestIds.ExportSourceItem);

    public ILocator ExportDone => page.GetByTestId(LayoutTestIds.ExportDone);

    public ILocator ExportDownload => page.GetByTestId(LayoutTestIds.ExportDownload);

    public ILocator ExportAgain => page.GetByTestId(LayoutTestIds.ExportAgain);

    public ILocator ExportLogToggle => page.GetByTestId(LayoutTestIds.ExportLogToggle);

    public ILocator ExportLog => page.GetByTestId(LayoutTestIds.ExportLog);
}
