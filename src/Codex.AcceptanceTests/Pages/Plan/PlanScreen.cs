namespace Codex.AcceptanceTests.Pages.Plan;

// The Plan screen: board, outline table and the Board / Outline control.
public sealed class PlanScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(PlanTestIds.Page);

    public ILocator Board => page.GetByTestId(PlanTestIds.Board);

    public ILocator Outline => page.GetByTestId(PlanTestIds.Outline);

    public ILocator ViewBoard => page.GetByTestId(PlanTestIds.ViewBoard);

    public ILocator ViewOutline => page.GetByTestId(PlanTestIds.ViewOutline);

    public ILocator LoadError => page.GetByTestId(PlanTestIds.LoadError);

    public ILocator Act(Guid actId) => page.GetByTestId(PlanTestIds.Act(actId));

    public ILocator ActToggle(Guid actId) => page.GetByTestId(PlanTestIds.ActToggle(actId));

    public ILocator ActMeta(Guid actId) => page.GetByTestId(PlanTestIds.ActMeta(actId));

    public ILocator ActTitle(Guid actId) => page.GetByTestId(PlanTestIds.ActTitle(actId));

    public ILocator ActRename(Guid actId) => page.GetByTestId(PlanTestIds.ActRename(actId));

    public ILocator ActMore(Guid actId) => page.GetByTestId(PlanTestIds.ActMore(actId));

    public ILocator ActDelete(Guid actId) => page.GetByTestId(PlanTestIds.ActDelete(actId));

    public ILocator ActOpener(Guid actId) => page.GetByTestId(PlanTestIds.ActOpener(actId));

    public ILocator ActOpenerThumb(Guid actId) => page.GetByTestId(PlanTestIds.ActOpenerThumb(actId));

    public ILocator OpenerDialog => page.GetByTestId(PlanTestIds.OpenerDialog);

    public ILocator OpenerCaption => page.GetByTestId(PlanTestIds.OpenerCaption);

    public ILocator OpenerSave => page.GetByTestId(PlanTestIds.OpenerSave);

    public ILocator NewAct => page.GetByTestId(PlanTestIds.NewAct);

    public ILocator Scope => page.GetByTestId(PlanTestIds.Scope);

    public ILocator Stats => page.GetByTestId(PlanTestIds.Stats);

    public ILocator NewChapter(Guid actId) => page.GetByTestId(PlanTestIds.NewChapter(actId));

    public ILocator Chapter(Guid chapterId) => page.GetByTestId(PlanTestIds.Chapter(chapterId));

    public ILocator ChapterHeadings => page.GetByTestId(PlanTestIds.ChapterHeading);

    public ILocator Scene(Guid sceneId) => page.GetByTestId(PlanTestIds.Scene(sceneId));

    public ILocator SceneNumber(Guid sceneId) => Scene(sceneId).GetByTestId(PlanTestIds.SceneNumber);

    public ILocator SceneNumbers => page.GetByTestId(PlanTestIds.SceneNumber);

    public ILocator SceneWords(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneWords(sceneId));

    public ILocator ScenePending(Guid sceneId) => page.GetByTestId(PlanTestIds.ScenePending(sceneId));

    public ILocator Label(Guid sceneId, Guid labelId) => page.GetByTestId(PlanTestIds.Label(sceneId, labelId));

    public ILocator Entry(Guid sceneId, Guid entryId) => page.GetByTestId(PlanTestIds.Entry(sceneId, entryId));

    public ILocator OutlineSceneNumbers => page.GetByTestId(PlanTestIds.OutlineSceneNumber);

    public ILocator OutlineScene(Guid sceneId) => page.GetByTestId(PlanTestIds.OutlineScene(sceneId));

    public ILocator DropLine => page.GetByTestId(PlanTestIds.DropLine);

    public ILocator Toast => page.GetByTestId(PlanTestIds.Toast);

    public ILocator ToastUndo => page.GetByTestId(PlanTestIds.ToastUndo);

    public ILocator SceneGrip(Guid sceneId) => page.GetByTestId(PlanTestIds.Grip("scene", sceneId));

    public ILocator ChapterGrip(Guid chapterId) => page.GetByTestId(PlanTestIds.Grip("chapter", chapterId));

    public ILocator SceneMore(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneMore(sceneId));

    public ILocator SceneArchive(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneArchive(sceneId));

    public ILocator ArchivedToggle => page.GetByTestId(PlanTestIds.ArchivedToggle);

    public ILocator ArchivedScene(Guid sceneId) => page.GetByTestId(PlanTestIds.ArchivedScene(sceneId));

    public ILocator Restore(Guid sceneId) => page.GetByTestId(PlanTestIds.Restore(sceneId));

    public ILocator Toolbar => page.GetByTestId(PlanTestIds.Toolbar);

    public ILocator SaveStatus => page.GetByTestId(PlanTestIds.SaveStatus);

    public ILocator NewScene(Guid chapterId) => page.GetByTestId(PlanTestIds.NewScene(chapterId));

    public ILocator SceneSummary(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneSummary(sceneId));

    public ILocator SceneSummaryEditor(Guid sceneId) => SceneSummary(sceneId).GetByTestId(PlanTestIds.SummaryEditor);

    public ILocator SceneTitle(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneTitle(sceneId));

    public ILocator MentionOption(Guid entryId) => page.GetByTestId(PlanTestIds.MentionOption(entryId));

    public ILocator SceneOpenInWrite(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneOpenInWrite(sceneId));

    public ILocator SceneLabels(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneLabels(sceneId));

    public ILocator LabelOption(Guid labelId) => page.GetByTestId(PlanTestIds.LabelOption(labelId));

    // What an empty card shows in place of the summary editor; clicking it opens the editor.
    public ILocator SceneSummaryEmpty(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneSummaryEmpty(sceneId));

    public ILocator ChapterTitle(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterTitle(chapterId));

    public ILocator ChapterTitleInput(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterTitleInput(chapterId));

    public ILocator ChapterTitleError(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterTitleError(chapterId));

    public ILocator ChapterMore(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterMore(chapterId));

    public ILocator ChapterRename(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterRename(chapterId));

    public ILocator ChapterDelete(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterDelete(chapterId));

    public ILocator ChapterEmpty(Guid chapterId) => page.GetByTestId(PlanTestIds.ChapterEmpty(chapterId));

    public ILocator SceneMove(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneMove(sceneId));

    public ILocator SceneMoveTarget(Guid chapterId) => page.GetByTestId(PlanTestIds.SceneMoveTarget(chapterId));

    public ILocator SceneMerge(Guid sceneId) => page.GetByTestId(PlanTestIds.SceneMerge(sceneId));

    public ILocator ArchivedList => page.GetByTestId(PlanTestIds.ArchivedList);

    public ILocator ArchivedChapter(Guid chapterId) => page.GetByTestId(PlanTestIds.ArchivedChapter(chapterId));

    public ILocator RestoreChapter(Guid chapterId) => page.GetByTestId(PlanTestIds.RestoreChapter(chapterId));

    public ILocator RestorePrompt => page.GetByTestId(PlanTestIds.RestorePrompt);

    public ILocator RestoreBoth => page.GetByTestId(PlanTestIds.RestoreBoth);

    public ILocator RestoreCancel => page.GetByTestId(PlanTestIds.RestoreCancel);

    // The label filter and its Manage labels panel.
    public ILocator LabelFilter => page.GetByTestId(PlanTestIds.LabelFilter);

    public ILocator LabelFilterPanel => page.GetByTestId(PlanTestIds.LabelFilterPanel);

    public ILocator LabelFilterOption(Guid labelId) => page.GetByTestId(PlanTestIds.LabelFilterOption(labelId));

    public ILocator ManageLabels => page.GetByTestId(PlanTestIds.ManageLabels);

    public ILocator LabelManager => page.GetByTestId(PlanTestIds.LabelManager);

    public ILocator LabelManagerDone => page.GetByTestId(PlanTestIds.LabelManagerDone);

    public ILocator LabelRename(Guid labelId) => page.GetByTestId(PlanTestIds.LabelRename(labelId));

    public ILocator LabelDelete(Guid labelId) => page.GetByTestId(PlanTestIds.LabelDelete(labelId));

    public ILocator LabelDeleteMessage => page.GetByTestId(PlanTestIds.LabelDeleteMessage);

    public ILocator LabelDeleteConfirm => page.GetByTestId(PlanTestIds.LabelDeleteConfirm);

    public ILocator LabelDeleteCancel => page.GetByTestId(PlanTestIds.LabelDeleteCancel);

    public ILocator LabelNewName => page.GetByTestId(PlanTestIds.LabelNewName);

    public ILocator LabelNewCreate => page.GetByTestId(PlanTestIds.LabelNewCreate);

    public ILocator LabelError => page.GetByTestId(PlanTestIds.LabelError);
}
