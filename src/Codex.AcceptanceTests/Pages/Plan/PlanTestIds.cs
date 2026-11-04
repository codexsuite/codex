namespace Codex.AcceptanceTests.Pages.Plan;

// Mirror of src/Codex.Client/src/features/plan/testIds.ts (prefix plan-).
public static class PlanTestIds
{
    public const string Page = "plan-page";
    public const string LoadError = "plan-load-error";
    public const string Toolbar = "plan-toolbar";
    public const string ViewBoard = "plan-view-board";
    public const string ViewOutline = "plan-view-outline";
    public const string Board = "plan-board";
    public const string Outline = "plan-outline";
    public const string ImportBanner = "plan-import-banner";
    public const string ChapterHeading = "plan-chapter-heading";
    public const string SceneNumber = "plan-scene-number";
    public const string OutlineSceneNumber = "plan-outline-scene-number";
    public const string DropLine = "plan-drop-line";
    public const string NewAct = "plan-new-act";
    public const string AddAct = "plan-add-act";
    public const string Scope = "plan-scope";
    public const string Stats = "plan-stats";
    public const string LabelFilter = "plan-label-filter";
    public const string LabelFilterPanel = "plan-label-filter-panel";
    public const string ManageLabels = "plan-manage-labels";
    public const string LabelManager = "plan-label-manager";
    public const string LabelManagerDone = "plan-label-manager-done";
    public const string LabelDeleteMessage = "plan-label-delete-message";
    public const string LabelDeleteConfirm = "plan-label-delete-confirm";
    public const string LabelDeleteCancel = "plan-label-delete-cancel";
    public const string LabelNewName = "plan-label-new-name";
    public const string LabelNewCreate = "plan-label-new-create";
    public const string LabelError = "plan-label-error";

    // The shared save status and label picker (src/editing/testIds.ts, prefix editing-), and the summary editor and mention
    // picker (src/prose/testIds.ts, prefix prose-), copied here until those modules have C# files.
    public const string SaveStatus = "editing-save-status";
    public const string SummaryEditor = "prose-summary-editor";

    // The shared toast (src/editing/testIds.ts, prefix editing-), copied here until the editing utilities have a C# file.
    public const string Toast = "editing-toast";
    public const string ToastUndo = "editing-toast-undo";

    public static string Act(Guid actId) => $"plan-act-{actId}";

    public static string ActToggle(Guid actId) => $"plan-act-toggle-{actId}";

    public static string ActMeta(Guid actId) => $"plan-act-meta-{actId}";

    public static string ActTitle(Guid actId) => $"plan-act-title-{actId}";

    public static string ActRename(Guid actId) => $"plan-act-rename-{actId}";

    public static string ActMore(Guid actId) => $"plan-act-more-{actId}";

    public static string ActDelete(Guid actId) => $"plan-act-delete-{actId}";

    public static string ActOpener(Guid actId) => $"plan-act-opener-{actId}";

    public static string ActOpenerThumb(Guid actId) => $"plan-act-opener-thumb-{actId}";

    public static string ChapterOpener(Guid chapterId) => $"plan-chapter-opener-{chapterId}";

    public static string ChapterOpenerThumb(Guid chapterId) => $"plan-chapter-opener-thumb-{chapterId}";

    public const string OpenerDialog = "plan-opener-dialog";
    public const string OpenerCaption = "plan-opener-caption";
    public const string OpenerSave = "plan-opener-save";
    public const string OpenerClear = "plan-opener-clear";

    public static string NewChapter(Guid actId) => $"plan-new-chapter-{actId}";

    public static string Chapter(Guid chapterId) => $"plan-chapter-{chapterId}";

    public static string Scene(Guid sceneId) => $"plan-scene-{sceneId}";

    public static string SceneSummary(Guid sceneId) => $"plan-scene-summary-{sceneId}";

    public static string SceneTitle(Guid sceneId) => $"plan-scene-title-{sceneId}";

    public static string SceneSummaryEmpty(Guid sceneId) => $"plan-scene-summary-empty-{sceneId}";

    public static string NewScene(Guid chapterId) => $"plan-new-scene-{chapterId}";

    public static string SceneOpenInWrite(Guid sceneId) => $"plan-scene-open-write-{sceneId}";

    public static string SceneLabels(Guid sceneId) => $"plan-scene-labels-{sceneId}";

    public static string MentionOption(Guid entryId) => $"prose-mention-option-{entryId}";

    public static string LabelOption(Guid labelId) => $"editing-label-option-{labelId}";

    public static string SceneWords(Guid sceneId) => $"plan-scene-words-{sceneId}";

    public static string ScenePending(Guid sceneId) => $"plan-scene-pending-{sceneId}";

    public static string Label(Guid sceneId, Guid labelId) => $"plan-label-{sceneId}-{labelId}";

    public static string Entry(Guid sceneId, Guid entryId) => $"plan-entry-{sceneId}-{entryId}";

    // kind is "act", "chapter" or "scene".
    public static string Grip(string kind, Guid id) => $"plan-grip-{kind}-{id}";

    public static string SceneMore(Guid sceneId) => $"plan-scene-more-{sceneId}";

    public static string SceneArchive(Guid sceneId) => $"plan-scene-archive-{sceneId}";

    public const string ArchivedToggle = "plan-archived-toggle";

    public const string ArchivedList = "plan-archived-list";

    public static string ArchivedScene(Guid sceneId) => $"plan-archived-scene-{sceneId}";

    public static string Restore(Guid sceneId) => $"plan-restore-{sceneId}";

    public static string OutlineAct(Guid actId) => $"plan-outline-act-{actId}";

    public static string OutlineChapter(Guid chapterId) => $"plan-outline-chapter-{chapterId}";

    public static string OutlineScene(Guid sceneId) => $"plan-outline-scene-{sceneId}";

    // Restructure (J16): chapter title and menu, scene move and merge, archived chapters and the restore prompt.
    public static string ChapterTitle(Guid chapterId) => $"plan-chapter-title-{chapterId}";

    public static string ChapterTitleInput(Guid chapterId) => $"plan-chapter-title-input-{chapterId}";

    public static string ChapterTitleError(Guid chapterId) => $"plan-chapter-title-error-{chapterId}";

    public static string ChapterMore(Guid chapterId) => $"plan-chapter-more-{chapterId}";

    public static string ChapterRename(Guid chapterId) => $"plan-chapter-rename-{chapterId}";

    public static string ChapterDelete(Guid chapterId) => $"plan-chapter-delete-{chapterId}";

    public static string ChapterEmpty(Guid chapterId) => $"plan-chapter-empty-{chapterId}";

    public static string SceneMove(Guid sceneId) => $"plan-scene-move-{sceneId}";

    public static string SceneMoveTarget(Guid chapterId) => $"plan-scene-move-target-{chapterId}";

    public static string SceneMerge(Guid sceneId) => $"plan-scene-merge-{sceneId}";

    public static string ArchivedChapter(Guid chapterId) => $"plan-archived-chapter-{chapterId}";

    public static string RestoreChapter(Guid chapterId) => $"plan-restore-chapter-{chapterId}";

    public const string RestorePrompt = "plan-restore-prompt";

    public const string RestoreBoth = "plan-restore-both";

    public const string RestoreCancel = "plan-restore-cancel";

    public static string LabelFilterOption(Guid labelId) => $"plan-label-option-{labelId}";

    public static string LabelRename(Guid labelId) => $"plan-label-rename-{labelId}";

    public static string LabelDelete(Guid labelId) => $"plan-label-delete-{labelId}";
}
