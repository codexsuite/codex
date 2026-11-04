namespace Codex.AcceptanceTests.Pages.Layout;

// Mirror of src/Codex.Client/src/features/layout/testIds.ts (prefix layout-).
public static class LayoutTestIds
{
    public const string Page = "layout-page";
    public const string LoadError = "layout-load-error";
    public const string PreviewError = "layout-preview-error";
    public const string ScopeSelect = "layout-scope-select";
    public const string TemplateSelect = "layout-template-select";
    public const string TrimSelect = "layout-trim-select";
    public const string ModeQuick = "layout-mode-quick";
    public const string ModeProof = "layout-mode-proof";
    public const string IssuesToggle = "layout-issues-toggle";
    public const string ViewSingle = "layout-view-single";
    public const string ViewSpread = "layout-view-spread";
    public const string Canvas = "layout-canvas";
    public const string PageRail = "layout-page-rail";
    public const string Status = "layout-status";
    public const string StatusPages = "layout-status-pages";
    public const string StatusRendering = "layout-status-rendering";
    public const string HouseBanner = "layout-house-banner";
    public const string DpiFlag = "layout-dpi-flag";
    public const string LatexMark = "layout-latex-mark";
    public const string BlockCard = "layout-block-card";
    public const string OpenInWrite = "layout-open-in-write";
    public const string Panel = "layout-panel";
    public const string IssuesList = "layout-issues-list";
    public const string IssueItem = "layout-issue-item";
    public const string ProofView = "layout-proof-view";
    public const string ProofBanner = "layout-proof-banner";
    public const string ProofSteps = "layout-proof-steps";
    public const string ProofPages = "layout-proof-pages";
    public const string ProofCached = "layout-proof-cached";
    public const string ProofOutOfDate = "layout-proof-out-of-date";
    public const string ProofDownload = "layout-proof-download";
    public const string ProofLogToggle = "layout-proof-log-toggle";
    public const string ProofLog = "layout-proof-log";
    public const string ProofRerender = "layout-proof-rerender";
    public const string ProofError = "layout-proof-error";

    public const string ExportOpen = "layout-export-open";
    public const string ExportDialog = "layout-export-dialog";
    public const string ExportCrop = "layout-export-crop";
    public const string ExportPreflight = "layout-export-preflight";
    public const string ExportVerdict = "layout-export-verdict";
    public const string ExportDraft = "layout-export-draft";
    public const string ExportSize = "layout-export-size";
    public const string ExportQueue = "layout-export-queue";
    public const string ExportCancelDialog = "layout-export-cancel-dialog";
    public const string ExportError = "layout-export-error";
    public const string PreflightList = "layout-preflight-list";
    public const string PreflightItem = "layout-preflight-item";
    public const string ExportsList = "layout-exports-list";
    public const string ExportRunning = "layout-export-running";
    public const string ExportSteps = "layout-export-steps";
    public const string ExportCancel = "layout-export-cancel";
    public const string ExportItem = "layout-export-item";
    public const string ExportSourceItem = "layout-export-source-item";
    public const string ExportDone = "layout-export-done";
    public const string ExportDownload = "layout-export-download";
    public const string ExportAgain = "layout-export-again";
    public const string ExportLogToggle = "layout-export-log-toggle";
    public const string ExportLog = "layout-export-log";

    public static string ExportBleed(string value) => $"layout-export-bleed-{value}";

    public static string ExportQuality(string value) => $"layout-export-quality-{value}";

    public static string ProofPage(int n) => $"layout-proof-page-{n}";

    public static string RailPage(int n) => $"layout-rail-page-{n}";

    public static string Tab(string name) => $"layout-tab-{name}";

    public static string Option(string key) => $"layout-option-{key}";
}
