namespace Codex.AcceptanceTests.Pages.References;

// Mirror of src/Codex.Client/src/features/references/testIds.ts; ReferencesTestIdsTests pins the constants.
public static class ReferencesTestIds
{
    public const string Page = "refs-page";
    public const string LoadError = "refs-load-error";
    public const string Strip = "refs-strip";
    public const string Meter = "refs-meter";
    public const string ClearedText = "refs-cleared-text";
    public const string Gate = "refs-gate";
    public const string ChipGroup = "refs-chips";
    public const string ViewSources = "refs-view-sources";
    public const string ViewBibliography = "refs-view-bibliography";
    public const string GroupSelect = "refs-grouping";
    public const string Search = "refs-search";
    public const string Stats = "refs-stats";
    public const string Table = "refs-table";
    public const string Empty = "refs-empty";
    public const string ClearFilters = "refs-clear-filters";
    public const string RowNumber = "refs-row-number";
    public const string RowStatus = "refs-row-status";
    public const string RowReason = "refs-row-reason";
    public const string Bibliography = "refs-bibliography";
    public const string BibStyle = "refs-bib-style";
    public const string BibEntry = "refs-bib-entry";
    public const string BibCredit = "refs-bib-credit";
    public const string BibError = "refs-bib-error";
    public const string ExportToggle = "refs-export-toggle";
    public const string Panel = "refs-panel";
    public const string PanelLoading = "refs-panel-loading";
    public const string PanelError = "refs-panel-error";
    public const string PanelClose = "refs-panel-close";
    public const string PanelMenu = "refs-panel-menu";
    public const string PanelMenuRemove = "refs-panel-menu-remove";
    public const string PanelMenuOpen = "refs-panel-menu-open";
    public const string RulesLink = "refs-rules-link";
    public const string PanelTitle = "refs-panel-title";
    public const string PanelBanner = "refs-panel-banner";
    public const string Verdict = "refs-verdict";
    public const string VerdictStatus = "refs-verdict-status";
    public const string VerdictReason = "refs-verdict-reason";
    public const string VerdictNote = "refs-verdict-note";
    public const string VerdictAction = "refs-verdict-action";
    public const string VerdictAsk = "refs-verdict-ask";
    public const string VerdictParaphrase = "refs-verdict-paraphrase";
    public const string SignedOff = "refs-signed-off";
    public const string FairUseForm = "refs-fair-use-form";
    public const string FairUseReason = "refs-fair-use-reason";
    public const string FairUseAccept = "refs-fair-use-accept";
    public const string Checks = "refs-checks";
    public const string Uses = "refs-uses";
    public const string UsesEmpty = "refs-uses-empty";
    public const string Details = "refs-details";
    public const string Recheck = "refs-recheck";
    public const string Entry = "refs-entry";
    public const string History = "refs-history";
    public const string HistoryItem = "refs-history-item";
    public const string Permission = "refs-permission";
    public const string PermStatus = "refs-perm-status";
    public const string PermTo = "refs-perm-to";
    public const string PermLetter = "refs-perm-letter";
    public const string PermRemind = "refs-perm-remind";
    public const string PermSentLabel = "refs-perm-sent-label";
    public const string PermRemindLabel = "refs-perm-remind-label";
    public const string PermRespondedLabel = "refs-perm-responded-label";
    public const string PermCredit = "refs-perm-credit";
    public const string PermNote = "refs-perm-note";
    public const string PermAttachment = "refs-perm-attachment";
    public const string PermDraft = "refs-perm-draft";
    public const string PermRemoveUses = "refs-perm-remove-uses";
    public const string PermCopy = "refs-perm-copy";
    public const string PermSend = "refs-perm-send";
    public const string PermWithdraw = "refs-perm-withdraw";
    public const string PermGranted = "refs-perm-granted";
    public const string PermRefused = "refs-perm-refused";
    public const string PermGrantForm = "refs-perm-grant-form";
    public const string PermFile = "refs-perm-file";
    public const string PermCreditInput = "refs-perm-credit-input";
    public const string PermTermsInput = "refs-perm-terms-input";
    public const string PermGrantSubmit = "refs-perm-grant-submit";
    public const string PermRefuseForm = "refs-perm-refuse-form";
    public const string PermRefuseNote = "refs-perm-refuse-note";
    public const string PermRefuseSubmit = "refs-perm-refuse-submit";
    public const string Remove = "refs-remove";
    public const string RemoveConfirm = "refs-remove-confirm";
    public const string RemoveCancel = "refs-remove-cancel";
    public const string AddOpen = "refs-add-open";
    public const string AddForm = "refs-add-form";
    public const string AddInput = "refs-add-input";
    public const string AddError = "refs-add-error";
    public const string AddMatch = "refs-add-match";
    public const string AddMatchTitle = "refs-add-match-title";
    public const string AddMoved = "refs-add-moved";
    public const string AddNotFound = "refs-add-not-found";
    public const string AddLookup = "refs-add-lookup";
    public const string AddUseExisting = "refs-add-use-existing";
    public const string AddManual = "refs-add-manual";
    public const string AddCancel = "refs-add-cancel";

    // The detour from Write (L12b): Add source's note and the return bar.
    public const string AddReturnNote = "refs-add-return-note";
    public const string ReturnBar = "refs-return-bar";
    public const string ReturnCite = "refs-return-cite";
    public const string ReturnCancel = "refs-return-cancel";

    public static string UseParaphrase(Guid useId) => $"refs-use-paraphrase-{useId}";
    public const string AddManualForm = "refs-add-manual-form";
    public const string AddSave = "refs-add-save";
    public const string AddImportOpen = "refs-add-import-open";
    public const string AddImportDialog = "refs-add-import-dialog";
    public const string AddImportFile = "refs-add-import-file";
    public const string AddImportSummary = "refs-add-import-summary";
    public const string AddImportProblem = "refs-add-import-problem";
    public const string AddImportSubmit = "refs-add-import-submit";
    public const string AddImportResult = "refs-add-import-result";
    public const string AddImportSkipped = "refs-add-import-skipped";
    public const string AddImportClose = "refs-add-import-close";

    public const string FlagInbox = "refs-flag-inbox";
    public const string FlagPanel = "refs-flag-panel";
    public const string FlagError = "refs-flag-error";
    public const string FlagEmpty = "refs-flag-empty";
    public const string FlagScene = "refs-flag-scene";
    public const string FlagWhy = "refs-flag-why";
    public const string FlagExcerpt = "refs-flag-excerpt";
    public const string FlagCite = "refs-flag-cite";
    public const string FlagOther = "refs-flag-other";
    public const string FlagOtherForm = "refs-flag-other-form";
    public const string FlagOtherNote = "refs-flag-other-note";
    public const string FlagOtherSubmit = "refs-flag-other-submit";
    public const string FlagOtherCancel = "refs-flag-other-cancel";
    public const string FlagNotQuote = "refs-flag-not-quote";
    public const string FlagWrite = "refs-flag-write";
    public const string FlagItemError = "refs-flag-item-error";

    public static string Chip(string filter) => $"refs-chip-{filter}";

    public static string ChipCount(string filter) => $"refs-chip-count-{filter}";


    public static string Group(string name) => $"refs-group-{name}";

    public static string Row(Guid sourceId) => $"refs-row-{sourceId}";

    public static string ExportLink(string format) => $"refs-export-{format}";

    public static string Check(string key) => $"refs-check-{key}";

    public static string Use(Guid useId) => $"refs-use-{useId}";

    public const string PanelMenuMerge = "refs-panel-menu-merge";
    public const string MergeDialog = "refs-merge-dialog";
    public const string MergeTarget = "refs-merge-target";
    public const string MergeSummary = "refs-merge-summary";
    public const string MergeConfirm = "refs-merge-confirm";
    public const string MergeCancel = "refs-merge-cancel";
    public const string PermCopyFollowUp = "refs-perm-copy-follow-up";
    public const string PermFollowedUp = "refs-perm-followed-up";
    public const string PermFollowedUpLabel = "refs-perm-followed-up-label";
    public const string PermFeeInput = "refs-perm-fee-input";
    public const string PermFormError = "refs-perm-form-error";
    public const string PermGrantCancel = "refs-perm-grant-cancel";
    public const string PermAttachmentRemove = "refs-perm-attachment-remove";
    public const string PermAttachmentConfirm = "refs-perm-attachment-confirm";
    public const string PermAttachInput = "refs-perm-attach-input";
    public const string VerdictCut = "refs-verdict-cut";
    public const string RowReminder = "refs-row-reminder";
    public const string RemindersChip = "refs-reminders-chip";
    public static string PermRemindIn(int days) => $"refs-perm-remind-in-{days}";
    public static string UseKind(Guid useId) => $"refs-use-kind-{useId}";

    public static string UseExcerpt(Guid useId) => $"refs-use-excerpt-{useId}";

    public static string UseNew(Guid useId) => $"refs-use-new-{useId}";

    public static string UseWrite(Guid useId) => $"refs-use-write-{useId}";

    public static string Field(string name) => $"refs-field-{name}";

    public static string Verification(string field) => $"refs-verify-{field}";

    public static string UpdateAddress(string field) => $"refs-update-address-{field}";

    public static string FlagItem(Guid flagId) => $"refs-flag-{flagId}";
    public static string AddField(string name) => $"refs-add-field-{name}";
}
