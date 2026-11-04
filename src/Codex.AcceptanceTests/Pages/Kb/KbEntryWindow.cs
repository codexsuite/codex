using System.Text.RegularExpressions;

namespace Codex.AcceptanceTests.Pages.Kb;

// The Knowledge base entry window over Plan and Write.
public sealed class KbEntryWindow(IPage page)
{
    public ILocator Root => page.GetByTestId(KbTestIds.EntryWindow);

    public ILocator Title => page.GetByTestId(KbTestIds.EntryWindowTitle);

    public ILocator Name => page.GetByTestId(KbTestIds.EntryName);

    public ILocator DraftName => page.GetByTestId(KbTestIds.DraftName);

    public ILocator Close => page.GetByTestId(KbTestIds.EntryWindowClose);

    public ILocator Expand => page.GetByTestId(KbTestIds.EntryWindowExpand);

    public ILocator Position => page.GetByTestId(KbTestIds.Position);

    public ILocator Tab(string name) => page.GetByTestId(KbTestIds.Tab(name));

    public ILocator Summary => page.GetByTestId(KbTestIds.Summary);

    public ILocator AddAlias => page.GetByTestId(KbTestIds.AddAlias);

    public ILocator AliasInput => page.GetByTestId(KbTestIds.AliasInput);

    public ILocator AliasError => page.GetByTestId(KbTestIds.AliasError);

    public ILocator Alias(string alias) => page.GetByTestId(KbTestIds.Alias(alias));

    public ILocator UseRow(Guid sceneId) => page.GetByTestId(KbTestIds.UseRow(sceneId));

    public ILocator UseKind(Guid sceneId, string kind) => page.GetByTestId(KbTestIds.UseKind(sceneId, kind));

    public ILocator OpenInWrite(Guid sceneId) => page.GetByTestId(KbTestIds.OpenInWrite(sceneId));

    public ILocator HighlightInWrite => page.GetByTestId(KbTestIds.HighlightInWrite);

    public ILocator TimelineNotes => page.GetByTestId(new Regex("^kb-timeline-note-"));

    public ILocator TimelineAdd => page.GetByTestId(KbTestIds.TimelineAdd);

    public ILocator TimelineAddScene => page.GetByTestId(KbTestIds.TimelineAddScene);

    public ILocator TimelineAddText => page.GetByTestId(KbTestIds.TimelineAddText);

    public ILocator TimelineAddSubmit => page.GetByTestId(KbTestIds.TimelineAddSubmit);

    public ILocator TimelineError => page.GetByTestId(KbTestIds.TimelineError);

    public ILocator TimelineEdit(Guid noteId) => page.GetByTestId(KbTestIds.TimelineEdit(noteId));

    public ILocator TimelineEditText(Guid noteId) => page.GetByTestId(KbTestIds.TimelineEditText(noteId));

    public ILocator TimelineEditSave(Guid noteId) => page.GetByTestId(KbTestIds.TimelineEditSave(noteId));

    public ILocator NewEntryButton(Guid categoryId) => page.GetByTestId(KbTestIds.NewEntry(categoryId));

    public ILocator Related => page.GetByTestId(KbTestIds.Related);

    public ILocator RelatedAdd => page.GetByTestId(KbTestIds.RelatedAdd);

    public ILocator RelatedSearch => page.GetByTestId(KbTestIds.RelatedSearch);

    public ILocator RelatedChip(Guid entryId) => page.GetByTestId(KbTestIds.RelatedChip(entryId));

    public ILocator RelatedRemove(Guid entryId) => page.GetByTestId(KbTestIds.RelatedRemove(entryId));

    public ILocator RelatedOption(Guid entryId) => page.GetByTestId(KbTestIds.RelatedOption(entryId));

    public ILocator RelatedError => page.GetByTestId(KbTestIds.RelatedError);

    public ILocator Menu => page.GetByTestId(KbTestIds.EntryMenu);

    public ILocator MenuDelete => page.GetByTestId(KbTestIds.EntryMenuDelete);

    public ILocator DeleteDialog => page.GetByTestId(KbTestIds.DeleteDialog);

    public ILocator DeleteMessage => page.GetByTestId(KbTestIds.DeleteMessage);

    public ILocator DeleteConfirm => page.GetByTestId(KbTestIds.DeleteConfirm);

    public ILocator DeleteCancel => page.GetByTestId(KbTestIds.DeleteCancel);

    public ILocator MakeMention(Guid sceneId) => page.GetByTestId(KbTestIds.MakeMention(sceneId));

    public ILocator UnlinkScene(Guid sceneId) => page.GetByTestId(KbTestIds.UnlinkScene(sceneId));

    public ILocator Toast => page.GetByTestId(KbTestIds.EditingToast);

    public ILocator ToastUndo => page.GetByTestId(KbTestIds.EditingToastUndo);

    public ILocator EntryError => page.GetByTestId(KbTestIds.EntryError);

    public ILocator DraftError => page.GetByTestId(KbTestIds.DraftError);

    public ILocator DraftCreateAnyway => page.GetByTestId(KbTestIds.DraftCreateAnyway);

    public ILocator RenameKeepAlias => page.GetByTestId(KbTestIds.RenameKeepAlias);

    public ILocator RenameAnyway => page.GetByTestId(KbTestIds.RenameAnyway);

    public ILocator AliasWarning => page.GetByTestId(KbTestIds.AliasWarning);

    public ILocator AliasAddAnyway => page.GetByTestId(KbTestIds.AliasAddAnyway);

    public ILocator CategoryChip => page.GetByTestId(KbTestIds.CategoryChip);

    public ILocator CategoryOption(Guid categoryId) => page.GetByTestId(KbTestIds.CategoryOption(categoryId.ToString()));

    public ILocator NewCategoryOpen => page.GetByTestId(KbTestIds.NewCategoryOpen);

    public ILocator NewCategoryName => page.GetByTestId(KbTestIds.NewCategoryName);

    public ILocator NewCategoryCreate => page.GetByTestId(KbTestIds.NewCategoryCreate);

    public ILocator NewCategoryError => page.GetByTestId(KbTestIds.NewCategoryError);

    public ILocator AlwaysInContext => page.GetByTestId(KbTestIds.AlwaysInContext);

    public ILocator UsedInTally => page.GetByTestId(KbTestIds.UsedInTally);

    public ILocator MenuMerge => page.GetByTestId(KbTestIds.EntryMenuMerge);

    public ILocator MergeDialog => page.GetByTestId(KbTestIds.MergeDialog);

    public ILocator MergeMessage => page.GetByTestId(KbTestIds.MergeMessage);

    public ILocator MergeOption(Guid entryId) => page.GetByTestId(KbTestIds.MergeOption(entryId));

    public ILocator MergeConfirm => page.GetByTestId(KbTestIds.MergeConfirm);

    public ILocator MergeCancel => page.GetByTestId(KbTestIds.MergeCancel);

    public ILocator MenuPromote => page.GetByTestId(KbTestIds.EntryMenuPromote);

    public ILocator ScopeBadge => page.GetByTestId(KbTestIds.ScopeBadge);

    public ILocator PromoteDialog => page.GetByTestId(KbTestIds.PromoteDialog);

    public ILocator PromoteConfirm => page.GetByTestId(KbTestIds.PromoteConfirm);

    public ILocator PromoteMerge => page.GetByTestId(KbTestIds.PromoteMerge);

    public ILocator PromoteCancel => page.GetByTestId(KbTestIds.PromoteCancel);
}
