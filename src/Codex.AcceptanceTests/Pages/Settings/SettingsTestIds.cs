namespace Codex.AcceptanceTests.Pages.Settings;

// Mirror of src/Codex.Client/src/features/settings/testIds.ts (prefix settings-), plus the editing- toast ids
// copied from src/Codex.Client/src/editing/testIds.ts.
public static class SettingsTestIds
{
    public const string Page = "settings-page";
    public const string LoadError = "settings-load-error";
    public const string Nav = "settings-nav";
    public const string SaveStatus = "settings-save-status";
    public const string Title = "settings-title";
    public const string TitleError = "settings-title-error";
    public const string Author = "settings-author";
    public const string Status = "settings-status";
    public const string Target = "settings-target";
    public const string Progress = "settings-progress";
    public const string ProgressText = "settings-progress-text";
    public const string SeriesSelect = "settings-series-select";
    public const string NumberSelect = "settings-number-select";
    public const string NumberLine = "settings-number-line";
    public const string Order = "settings-series-order";
    public const string OrderPublishedWarning = "settings-order-published-warning";
    public const string MoveNote = "settings-move-note";
    public const string MoveReview = "settings-move-review";
    public const string MoveDialog = "settings-move-dialog";
    public const string MoveFromTo = "settings-move-from-to";
    public const string MoveNotes = "settings-move-notes";
    public const string MoveWarning = "settings-move-warning";
    public const string MoveConfirm = "settings-move-confirm";
    public const string MoveCancel = "settings-move-cancel";
    public const string MoveError = "settings-move-error";
    public const string Guide = "settings-style-guide";
    public const string GuideWords = "settings-style-guide-words";
    public const string SeriesGuide = "settings-series-guide";

    public const string QuoteLimit = "settings-quote-limit";
    public const string QuoteLimitError = "settings-quote-limit-error";
    public const string ImpactSummary = "settings-impact-summary";
    public const string Publisher = "settings-publisher";
    public const string PlannedYear = "settings-planned-year";
    public const string Letter = "settings-letter";
    public const string LetterMissing = "settings-letter-missing";
    public const string PublicationError = "settings-publication-error";
    public const string LayoutTemplate = "settings-layout-template";
    public const string LayoutGate = "settings-layout-gate";
    public const string LayoutNone = "settings-layout-none";
    public const string ArchivedBar = "settings-archived-bar";
    public const string Duplicate = "settings-duplicate";
    public const string DuplicateDialog = "settings-duplicate-dialog";
    public const string DuplicateTitle = "settings-duplicate-title";
    public const string DuplicateConfirm = "settings-duplicate-confirm";
    public const string DuplicateDone = "settings-duplicate-done";
    public const string ExportSource = "settings-export-source";
    public const string ExportStatus = "settings-export-status";
    public const string ExportDownload = "settings-export-download";
    public const string ExportNotes = "settings-export-notes";
    public const string ExportManuscript = "settings-export-manuscript";
    public const string ManuscriptDialog = "settings-manuscript-dialog";
    public const string ManuscriptScope = "settings-manuscript-scope";
    public const string ManuscriptChapter = "settings-manuscript-chapter";
    public const string ManuscriptBeats = "settings-manuscript-beats";
    public const string ManuscriptSummaries = "settings-manuscript-summaries";
    public const string ManuscriptRisk = "settings-manuscript-risk";
    public const string ManuscriptBusy = "settings-manuscript-busy";
    public const string ManuscriptError = "settings-manuscript-error";
    public const string ManuscriptSubmit = "settings-manuscript-submit";
    public const string ManuscriptCancel = "settings-manuscript-cancel";
    public const string ManuscriptJob = "settings-manuscript-job";
    public const string ManuscriptJobStatus = "settings-manuscript-job-status";
    public const string ManuscriptDownload = "settings-manuscript-download";
    public const string ManuscriptJobError = "settings-manuscript-job-error";
    public const string ManuscriptJobNotes = "settings-manuscript-job-notes";
    public const string ManuscriptRetry = "settings-manuscript-retry";
    public const string Archive = "settings-archive";
    public const string ArchiveDialog = "settings-archive-dialog";
    public const string ArchiveConfirm = "settings-archive-confirm";
    public const string Restore = "settings-restore";
    public const string LifecycleError = "settings-lifecycle-error";

    public const string PublishDialog = "settings-publish-dialog";
    public const string PublishMessage = "settings-publish-message";
    public const string PublishWarning = "settings-publish-warning";
    public const string PublishWaiting = "settings-publish-waiting";
    public const string PublishConfirm = "settings-publish-confirm";
    public const string PublishCancel = "settings-publish-cancel";
    public const string PublishError = "settings-publish-error";
    public const string Snapshots = "settings-snapshots";
    public const string SnapshotsHeading = "settings-snapshots-heading";
    public const string NewEdition = "settings-new-edition";
    public const string DuplicateCancel = "settings-duplicate-cancel";
    public const string DuplicateError = "settings-duplicate-error";

    public const string Sharing = "settings-sharing";
    public const string SharingPerson = "settings-sharing-person";
    public const string SharingRole = "settings-sharing-role";
    public const string SharingRemove = "settings-sharing-remove";
    public const string SharingBookList = "settings-sharing-book-list";
    public const string SharingEmail = "settings-sharing-email";
    public const string SharingMatch = "settings-sharing-match";
    public const string SharingMatchRole = "settings-sharing-match-role";
    public const string SharingAdd = "settings-sharing-add";

    public const string Toast = "editing-toast";
    public const string ToastUndo = "editing-toast-undo";

    public static string NavItem(string section) => $"settings-nav-{section}";

    public static string Card(string section) => $"settings-section-{section}";

    public static string StatusOption(string status) => $"settings-status-{status}";

    public static string OrderRow(string bookId) => $"settings-order-{bookId}";

    public static string OrderWas(string bookId) => $"settings-order-was-{bookId}";

    public static string MoveStep(string key) => $"settings-move-step-{key}";

    public static string ChipFor(string field) => $"settings-chip-{field}";

    public static string CitationStyleOption(string style) => $"settings-citation-style-{style}";

    public static string Format(string code) => $"settings-format-{code}";

    public const string LayoutTemplatePick = "settings-layout-template-pick";

    public static string LayoutTrim(string trim) => $"settings-layout-trim-{trim}";

    public const string LayoutChangelog = "settings-layout-changelog";
    public const string LayoutUpgradeApply = "settings-layout-upgrade-apply";
    public const string LayoutForkName = "settings-layout-fork-name";
    public const string LayoutForkConfirm = "settings-layout-fork-confirm";
    public const string LayoutTemplateError = "settings-layout-template-error";
    public const string LayoutTemplatesToggle = "settings-layout-templates-toggle";

    public static string LayoutTemplateCard(string slug) => $"settings-layout-template-card-{slug}";

    public static string LayoutTemplateUse(string slug) => $"settings-layout-template-use-{slug}";

    public static string LayoutTemplateFork(string slug) => $"settings-layout-template-fork-{slug}";

    public static string LayoutTemplateUploadVersion(string slug) => $"settings-layout-template-upload-version-{slug}";

    public static string ManuscriptFormat(string format) => $"settings-manuscript-format-{format}";
}
