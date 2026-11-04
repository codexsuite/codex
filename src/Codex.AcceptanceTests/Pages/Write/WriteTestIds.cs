namespace Codex.AcceptanceTests.Pages.Write;

// Mirror of src/Codex.Client/src/features/write/testIds.ts (prefix write-).
public static class WriteTestIds
{
    public const string Page = "write-page";
    public const string LoadError = "write-load-error";
    public const string Empty = "write-empty";
    public const string Manuscript = "write-manuscript";
    public const string ChapterJump = "write-chapter-jump";
    public const string ChapterRef = "write-chapter-ref";
    public const string Minimap = "write-minimap";
    public const string Toolbar = "write-toolbar";
    public const string ScopeSelect = "write-scope-select";
    public const string ScopeCounts = "write-scope-counts";
    public const string FormatButton = "write-format-button";
    public const string FormatPopover = "write-format-popover";
    public const string FocusButton = "write-focus-button";

    // The shared save status in the book bar (editing/testIds.ts, prefix editing-).
    public const string SaveStatus = "editing-save-status";
    public const string SaveStatusReload = "editing-save-status-reload";
    public const string SaveStatusRetry = "editing-save-status-retry";

    // The editors inside a scene (prose/testIds.ts, prefix prose-).
    public const string ProseEditor = "prose-editor";
    public const string SummaryEditor = "prose-summary-editor";

    // The Rich / Source toggle around each editor, and the CodeMirror source editor (prose/testIds.ts).
    public const string ModeRich = "prose-mode-rich-scene-prose";
    public const string ModeSource = "prose-mode-source-scene-prose";
    public const string SourceEditor = "prose-source-editor-scene-prose";

    // The slash menu items (prose/testIds.ts: prose-slash-item-<id>) Write registers.
    public const string SlashSceneBeat = "prose-slash-item-scene-beat";
    public const string SlashChapterReference = "prose-slash-item-chapter-reference";
    public const string SlashNewSceneFromHere = "prose-slash-item-new-scene-from-here";
    public const string SlashContinueWriting = "prose-slash-item-continue-writing";
    public const string SlashTable = "prose-slash-item-table";

    // The table toolbar that floats over the table the caret is in (prose/testIds.ts).
    public const string Table = "prose-table";
    public const string TableHeaderCell = "prose-table-header-cell";
    public const string TableAddColumnAfter = "prose-table-addColumnAfter";
    public const string ChapterPicker = "write-chapter-picker";
    public const string BeatContextPicker = "write-beat-context-picker";
    public const string BeatContextSearch = "write-beat-context-search";
    public const string DropLine = "write-drop-line";

    // The shared toast (editing/testIds.ts, prefix editing-) and label picker.
    public const string Toast = "editing-toast";
    public const string ToastUndo = "editing-toast-undo";

    public static string ChapterPickerOption(Guid chapterId) => $"write-chapter-picker-{chapterId}";

    public static string BeatGrip(Guid beatId) => $"write-beat-grip-{beatId}";

    public static string BeatGenerate(Guid beatId) => $"write-beat-generate-{beatId}";

    public static string Draft(string target) => $"write-draft-{target}";

    public static string DraftText(string target) => $"write-draft-text-{target}";

    public static string DraftCaret(string target) => $"write-draft-caret-{target}";

    public const string DraftPicker = "write-draft-picker";

    public static string DraftTemplate(string templateId) => $"write-draft-template-{templateId}";

    public static string DraftModel(Guid integrationId, string modelId) => $"write-draft-model-{integrationId}-{modelId}";

    public const string DraftConnect = "write-draft-connect";

    public static string BeatChoice(Guid beatId) => $"write-beat-choice-{beatId}";

    public static string BeatBody(Guid beatId) => $"write-beat-body-{beatId}";

    public static string BeatShow(Guid beatId) => $"write-beat-show-{beatId}";

    public static string DraftMeta(string target) => $"write-draft-meta-{target}";
    public static string DraftNote(string target) => $"write-draft-note-{target}";

    public static string DraftError(string target) => $"write-draft-error-{target}";

    public static string DraftStop(string target) => $"write-draft-stop-{target}";

    public static string DraftApply(string target) => $"write-draft-apply-{target}";

    public static string DraftRetry(string target) => $"write-draft-retry-{target}";

    public static string DraftDiscard(string target) => $"write-draft-discard-{target}";
    public static string DraftSent(string target) => $"write-draft-sent-{target}";
    public static string DraftSentPopover(string target) => $"write-draft-sent-popover-{target}";

    public static string SceneLabelAdd(Guid sceneId) => $"write-scene-label-add-{sceneId}";

    public static string LabelOption(Guid labelId) => $"editing-label-option-{labelId}";

    public static string BeatHide(Guid beatId) => $"write-beat-hide-{beatId}";

    public static string BeatLength(Guid beatId, int words) => $"write-beat-length-{beatId}-{words}";

    public static string BeatContextAdd(Guid beatId) => $"write-beat-context-add-{beatId}";

    public static string BeatContextChip(Guid beatId, Guid entryId) => $"write-beat-context-{beatId}-{entryId}";

    public static string BeatContextOption(Guid entryId) => $"write-beat-context-option-{entryId}";

    public static string FormatOption(string group, string value) => $"write-format-{group}-{value}";

    public static string ActMarker(Guid actId) => $"write-act-{actId}";

    public static string Chapter(Guid chapterId) => $"write-chapter-{chapterId}";

    public static string ChapterTitle(Guid chapterId) => $"write-chapter-title-{chapterId}";

    public static string ChapterCounts(Guid chapterId) => $"write-chapter-counts-{chapterId}";

    public static string ChapterCopyRef(Guid chapterId) => $"write-chapter-copy-ref-{chapterId}";

    public static string Scene(Guid sceneId) => $"write-scene-{sceneId}";

    public static string SceneHeading(Guid sceneId) => $"write-scene-heading-{sceneId}";

    public static string SceneSummary(Guid sceneId) => $"write-scene-summary-{sceneId}";

    public static string SceneLabel(Guid sceneId) => $"write-scene-label-{sceneId}";

    public static string SceneEntry(Guid sceneId) => $"write-scene-entry-{sceneId}";

    public static string SceneWords(Guid sceneId) => $"write-scene-words-{sceneId}";

    public static string Divider(Guid sceneId) => $"write-divider-{sceneId}";

    public static string MinimapTick(Guid sceneId) => $"write-minimap-tick-{sceneId}";

    public static string MinimapChapter(Guid chapterId) => $"write-minimap-chapter-{chapterId}";

    // The ids of every minimap tick / chapter mark, for counting (Playwright takes a regex for a test id).
    public static System.Text.RegularExpressions.Regex AnyMinimapTick() => new("^write-minimap-tick-");

    public static System.Text.RegularExpressions.Regex AnyMinimapChapter() => new("^write-minimap-chapter-");

    // Knowledge base in the prose, the highlight chip and Codex addition cards.
    public const string KbMention = "write-kb-mention";
    public const string KbAlias = "write-kb-alias";
    public const string SlashCodexAddition = "prose-slash-item-codex-addition";
    public const string EntryPicker = "write-entry-picker";
    public const string HighlightChip = "write-highlight-chip";
    public const string HighlightName = "write-highlight-name";
    public const string HighlightPosition = "write-highlight-position";
    public const string HighlightPrevious = "write-highlight-previous";
    public const string HighlightNext = "write-highlight-next";
    public const string HighlightClear = "write-highlight-clear";
    public static string Marker(Guid useId) => $"write-marker-{useId}";

    // Matches every marker's id, for counting.
    public static System.Text.RegularExpressions.Regex AnyMarker() => new("^write-marker-[0-9a-f]{8}-");

    public const string MarkerCard = "write-marker-card";
    public const string MarkerCardSource = "write-marker-card-source";
    public const string MarkerCardStatus = "write-marker-card-status";
    public const string MarkerCardReason = "write-marker-card-reason";
    public const string MarkerCardWords = "write-marker-card-words";
    public const string MarkerCardOpen = "write-marker-card-open";
    public const string AdditionCard = "prose-directive-kb-addition";

    public static string EntryPickerOption(Guid entryId) => $"write-entry-picker-{entryId}";

    public static string Addition(Guid additionId) => $"write-addition-{additionId}";

    public static string AdditionBody(Guid additionId) => $"write-addition-body-{additionId}";

    public static string AdditionEntry(Guid additionId) => $"write-addition-entry-{additionId}";

    public const string FormatPreview = "write-format-preview";

    public static string Figure(Guid figureId) => $"write-figure-{figureId}";

    public static string FigureImage(Guid figureId) => $"write-figure-image-{figureId}";

    public static string FigureCaption(Guid figureId) => $"write-figure-caption-{figureId}";

    public static string FigureLowDpi(Guid figureId) => $"write-figure-low-dpi-{figureId}";

    public static string FigureMissing(Guid figureId) => $"write-figure-missing-{figureId}";

    public static string FigureRemove(Guid figureId) => $"write-figure-remove-{figureId}";

    public static string FigureEdit(Guid figureId) => $"write-figure-edit-{figureId}";

    // Insert figure… (`/` → Figure…) and the card's Edit dialog.
    public const string SlashFigure = "prose-slash-item-figure";
    public const string FigureDialog = "write-figure-dialog";
    public const string FigureDialogSubmit = "write-figure-dialog-submit";
    public const string FigureCaptionInput = "write-figure-dialog-caption";
    public const string FigureAltInput = "write-figure-dialog-alt";

    public static string FigurePlacement(string placement) => $"write-figure-dialog-placement-{placement}";

    // The Preview split pane (#13). Format's Show/Hide are FormatOption("pane", "show" | "hide").
    public const string Preview = "write-preview";
    public const string PreviewClose = "write-preview-close";
    public const string PreviewTemplate = "write-preview-template";
    public const string PreviewScope = "write-preview-scope";
    public const string PreviewPages = "write-preview-pages";
    public const string PreviewRendering = "write-preview-rendering";
    public const string PreviewInLayout = "write-preview-in-layout";
    public const string PreviewHouse = "write-preview-house";
    public const string PreviewCanvas = "write-preview-canvas";
    public const string PreviewUnavailable = "write-preview-unavailable";
    public const string PreviewError = "write-preview-error";

    // Scene ⋯ menu and Version history (L13.1).
    public static string SceneMore(Guid sceneId) => $"write-scene-more-{sceneId}";

    public static string SceneMenu(Guid sceneId) => $"write-scene-menu-{sceneId}";

    public static string SceneHistory(Guid sceneId) => $"write-scene-history-{sceneId}";

    public const string History = "write-history";
    public const string HistoryClose = "write-history-close";
    public const string HistoryList = "write-history-list";
    public const string HistoryCurrent = "write-history-current";
    public const string HistoryCompare = "write-history-compare";
    public const string HistoryAdded = "write-history-added";
    public const string HistoryRemoved = "write-history-removed";
    public const string HistoryDelta = "write-history-delta";
    public const string HistoryRestore = "write-history-restore";
    public const string HistoryConfirm = "write-history-confirm";
    public const string HistoryConfirmText = "write-history-confirm-text";
    public const string HistoryCancel = "write-history-cancel";
    public const string HistoryUpdated = "write-history-updated";
    public const string HistoryReadOnly = "write-history-read-only";

    public static string HistoryRow(Guid revisionId) => $"write-history-row-{revisionId}";

    public static System.Text.RegularExpressions.Regex AnyHistoryRow() => new("^write-history-row-");

    // Find and replace (L13.3).
    public const string Find = "write-find";
    public const string FindCount = "write-find-count";
    public const string ReplaceToggle = "write-replace-toggle";
    public const string ReplacePanel = "write-replace-panel";
    public const string ReplaceInput = "write-replace-input";
    public const string ReplaceProse = "write-replace-prose";
    public const string ReplaceSummaries = "write-replace-summaries";
    public const string ReplaceBeats = "write-replace-beats";
    public const string ReplaceMatchCase = "write-replace-match-case";
    public const string ReplaceWholeWord = "write-replace-whole-word";
    public const string ReplaceArchived = "write-replace-archived";
    public const string ReplaceCount = "write-replace-count";
    public const string ReplaceDrafts = "write-replace-drafts";
    public const string ReplaceOne = "write-replace-one";
    public const string ReplaceNext = "write-replace-next";
    public const string ReplacePrevious = "write-replace-previous";
    public const string ReplaceAll = "write-replace-all";

    public static string ReplaceItem(int index) => $"write-replace-item-{index}";

    public static System.Text.RegularExpressions.Regex AnyReplaceItem() => new("^write-replace-item-");
}
