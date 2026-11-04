namespace Codex.AcceptanceTests.Pages.Write;

// The Write screen: the manuscript, its scenes and chapters, the minimap and the chapter jump.
public sealed class WritePageObject(IPage page)
{
    public ILocator Root => page.GetByTestId(WriteTestIds.Page);

    public ILocator Manuscript => page.GetByTestId(WriteTestIds.Manuscript);

    public ILocator LoadError => page.GetByTestId(WriteTestIds.LoadError);

    public ILocator ChapterJump => page.GetByTestId(WriteTestIds.ChapterJump);

    public ILocator ScopeSelect => page.GetByTestId(WriteTestIds.ScopeSelect);

    public ILocator ScopeCounts => page.GetByTestId(WriteTestIds.ScopeCounts);

    public ILocator FormatButton => page.GetByTestId(WriteTestIds.FormatButton);

    public ILocator FormatPopover => page.GetByTestId(WriteTestIds.FormatPopover);

    public ILocator FormatOption(string group, string value) => page.GetByTestId(WriteTestIds.FormatOption(group, value));

    public ILocator FocusButton => page.GetByTestId(WriteTestIds.FocusButton);

    // "Saved", "Saving…", "Unsaved" or the "Conflict · Reload" banner after a stale save; `data-state` is the machine value.
    public ILocator SaveStatus => page.GetByTestId(WriteTestIds.SaveStatus);

    public ILocator SaveStatusReload => page.GetByTestId(WriteTestIds.SaveStatusReload);

    // "Unsaved · Retry": shown after the server refused a save; saves the kept edit again.
    public ILocator SaveStatusRetry => page.GetByTestId(WriteTestIds.SaveStatusRetry);

    // The scene's prose editor (contenteditable).
    public ILocator ProseEditor(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.ProseEditor);

    // The scene's summary editor in the gutter.
    public ILocator SummaryEditor(Guid sceneId) => SceneSummary(sceneId).GetByTestId(WriteTestIds.SummaryEditor);

    // The scene's Rich / Source switch, and its CodeMirror source editor while Source shows.
    public ILocator ModeRich(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.ModeRich);

    public ILocator ModeSource(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.ModeSource);

    public ILocator SourceEditor(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.SourceEditor);

    public ILocator Toolbar => page.GetByTestId(WriteTestIds.Toolbar);

    // The stored Format settings for a book, or null when none are stored.
    public Task<string?> StoredFormatAsync(string bookId) =>
        page.EvaluateAsync<string?>("id => window.localStorage.getItem('codex.format.' + id)", bookId);

    public ILocator Minimap => page.GetByTestId(WriteTestIds.Minimap);

    public ILocator MinimapTicks => page.GetByTestId(WriteTestIds.AnyMinimapTick());

    public ILocator MinimapChapters => page.GetByTestId(WriteTestIds.AnyMinimapChapter());

    // Every chapter link drawn in the manuscript (`§ Chapter 5` or `Missing chapter`).
    public ILocator ChapterRefs => page.GetByTestId(WriteTestIds.ChapterRef);

    public ILocator Scene(Guid sceneId) => page.GetByTestId(WriteTestIds.Scene(sceneId));

    public ILocator ChapterRefsIn(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.ChapterRef);

    public ILocator ActMarker(Guid actId) => page.GetByTestId(WriteTestIds.ActMarker(actId));

    public ILocator ChapterTitle(Guid chapterId) => page.GetByTestId(WriteTestIds.ChapterTitle(chapterId));

    public ILocator ChapterCounts(Guid chapterId) => page.GetByTestId(WriteTestIds.ChapterCounts(chapterId));

    public ILocator ChapterCopyRef(Guid chapterId) => page.GetByTestId(WriteTestIds.ChapterCopyRef(chapterId));

    public ILocator SceneHeading(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneHeading(sceneId));

    public ILocator SceneSummary(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneSummary(sceneId));

    public ILocator SceneLabels(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneLabel(sceneId));

    public ILocator SceneEntries(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneEntry(sceneId));

    public ILocator SceneWords(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneWords(sceneId));

    public ILocator MinimapTick(Guid sceneId) => page.GetByTestId(WriteTestIds.MinimapTick(sceneId));

    public ILocator Dividers => page.GetByTestId(new System.Text.RegularExpressions.Regex("^write-divider-"));

    // How far the manuscript has scrolled, in pixels.
    public Task<double> ScrollTopAsync() => Manuscript.EvaluateAsync<double>("element => element.scrollTop");

    // The prose editor's `/` menu item and the pickers it opens.
    public ILocator SlashItem(string testId) => page.GetByTestId(testId);

    // The tables in a scene's prose editor, and their header cells.
    public ILocator Tables(Guid sceneId) => ProseEditor(sceneId).GetByTestId(WriteTestIds.Table);

    public ILocator TableHeaderCells(Guid sceneId) => ProseEditor(sceneId).GetByTestId(WriteTestIds.TableHeaderCell);

    public ILocator TableAddColumnAfter => page.GetByTestId(WriteTestIds.TableAddColumnAfter);

    public ILocator ChapterPickerOption(Guid chapterId) => page.GetByTestId(WriteTestIds.ChapterPickerOption(chapterId));

    // Beat cards drawn in a scene, and the parts of one card.
    public ILocator BeatCards(Guid sceneId) => Scene(sceneId).GetByTestId("prose-directive-beat");

    public ILocator BeatGrip(Guid beatId) => page.GetByTestId(WriteTestIds.BeatGrip(beatId));

    public ILocator BeatLength(Guid beatId, int words) => page.GetByTestId(WriteTestIds.BeatLength(beatId, words));

    public ILocator BeatContextAdd(Guid beatId) => page.GetByTestId(WriteTestIds.BeatContextAdd(beatId));

    public ILocator BeatContextPicker => page.GetByTestId(WriteTestIds.BeatContextPicker);

    public ILocator BeatContextOption(Guid entryId) => page.GetByTestId(WriteTestIds.BeatContextOption(entryId));

    public ILocator BeatContextChip(Guid beatId, Guid entryId) => page.GetByTestId(WriteTestIds.BeatContextChip(beatId, entryId));

    // Every scene row on the screen.
    public ILocator Scenes => page.GetByTestId(new System.Text.RegularExpressions.Regex("^write-scene-[0-9a-f-]{36}$"));

    public ILocator BeatContextSearch => page.GetByTestId(WriteTestIds.BeatContextSearch);

    // The AI draft panel under a beat (`target` is the beat's id) and its parts.
    public ILocator Draft(Guid beatId) => page.GetByTestId(WriteTestIds.Draft(beatId.ToString()));

    public ILocator DraftText(Guid beatId) => page.GetByTestId(WriteTestIds.DraftText(beatId.ToString()));

    public ILocator DraftCaret(Guid beatId) => page.GetByTestId(WriteTestIds.DraftCaret(beatId.ToString()));

    public ILocator BeatBody(Guid beatId) => page.GetByTestId(WriteTestIds.BeatBody(beatId));

    // The split button's template-and-model choice, and the picker it opens.
    public ILocator BeatChoice(Guid beatId) => page.GetByTestId(WriteTestIds.BeatChoice(beatId));

    public ILocator DraftPicker => page.GetByTestId(WriteTestIds.DraftPicker);

    public ILocator DraftTemplate(string templateId) => page.GetByTestId(WriteTestIds.DraftTemplate(templateId));

    public ILocator DraftModel(Guid integrationId, string modelId) => page.GetByTestId(WriteTestIds.DraftModel(integrationId, modelId));

    public ILocator DraftConnect => page.GetByTestId(WriteTestIds.DraftConnect);

    public ILocator DraftNote(Guid beatId) => page.GetByTestId(WriteTestIds.DraftNote(beatId.ToString()));
    public ILocator DraftMeta(Guid beatId) => page.GetByTestId(WriteTestIds.DraftMeta(beatId.ToString()));

    public ILocator DraftError(Guid beatId) => page.GetByTestId(WriteTestIds.DraftError(beatId.ToString()));

    public ILocator DraftStop(Guid beatId) => page.GetByTestId(WriteTestIds.DraftStop(beatId.ToString()));

    public ILocator DraftApply(Guid beatId) => page.GetByTestId(WriteTestIds.DraftApply(beatId.ToString()));

    public ILocator DraftRetry(Guid beatId) => page.GetByTestId(WriteTestIds.DraftRetry(beatId.ToString()));

    public ILocator DraftDiscard(Guid beatId) => page.GetByTestId(WriteTestIds.DraftDiscard(beatId.ToString()));

    public ILocator DraftSent(Guid beatId) => page.GetByTestId(WriteTestIds.DraftSent(beatId.ToString()));

    public ILocator DraftSentPopover(Guid beatId) => page.GetByTestId(WriteTestIds.DraftSentPopover(beatId.ToString()));

    public ILocator BeatGenerate(Guid beatId) => page.GetByTestId(WriteTestIds.BeatGenerate(beatId));

    public ILocator Toast => page.GetByTestId(WriteTestIds.Toast);

    public ILocator ToastUndo => page.GetByTestId(WriteTestIds.ToastUndo);

    public ILocator SceneLabelAdd(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneLabelAdd(sceneId));

    public ILocator LabelOption(Guid labelId) => page.GetByTestId(WriteTestIds.LabelOption(labelId));

    // Mentions and plain-text name matches drawn in a scene's prose.
    public ILocator Mentions(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.KbMention);

    public ILocator Aliases(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.KbAlias);

    // The toolbar chip for the highlighted entry and its parts.
    public ILocator HighlightChip => page.GetByTestId(WriteTestIds.HighlightChip);

    public ILocator HighlightName => page.GetByTestId(WriteTestIds.HighlightName);

    public ILocator HighlightPosition => page.GetByTestId(WriteTestIds.HighlightPosition);

    public ILocator HighlightPrevious => page.GetByTestId(WriteTestIds.HighlightPrevious);

    public ILocator HighlightNext => page.GetByTestId(WriteTestIds.HighlightNext);

    public ILocator HighlightClear => page.GetByTestId(WriteTestIds.HighlightClear);

    // Source markers (the superscript numbers) and the card one opens.
    public ILocator Markers => page.GetByTestId(WriteTestIds.AnyMarker());

    public ILocator Marker(Guid useId) => page.GetByTestId(WriteTestIds.Marker(useId));

    public ILocator MarkerCard => page.GetByTestId(WriteTestIds.MarkerCard);

    public ILocator MarkerCardSource => page.GetByTestId(WriteTestIds.MarkerCardSource);

    public ILocator MarkerCardStatus => page.GetByTestId(WriteTestIds.MarkerCardStatus);

    public ILocator MarkerCardReason => page.GetByTestId(WriteTestIds.MarkerCardReason);

    public ILocator MarkerCardWords => page.GetByTestId(WriteTestIds.MarkerCardWords);

    public ILocator MarkerCardOpen => page.GetByTestId(WriteTestIds.MarkerCardOpen);

    // The entry picker the Codex addition item opens, and the addition cards in a scene.
    public ILocator EntryPickerOption(Guid entryId) => page.GetByTestId(WriteTestIds.EntryPickerOption(entryId));

    public ILocator AdditionCards(Guid sceneId) => Scene(sceneId).GetByTestId(WriteTestIds.AdditionCard);

    public ILocator AdditionBody(Guid additionId) => page.GetByTestId(WriteTestIds.AdditionBody(additionId));

    public ILocator AdditionEntry(Guid additionId) => page.GetByTestId(WriteTestIds.AdditionEntry(additionId));

    // Version history (L13.1): the scene's ⋯ menu, the panel, its rows and the comparison.
    public ILocator SceneMore(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneMore(sceneId));

    public ILocator SceneHistory(Guid sceneId) => page.GetByTestId(WriteTestIds.SceneHistory(sceneId));

    public ILocator History => page.GetByTestId(WriteTestIds.History);

    public ILocator HistoryRows => page.GetByTestId(WriteTestIds.AnyHistoryRow());

    public ILocator HistoryRow(Guid revisionId) => page.GetByTestId(WriteTestIds.HistoryRow(revisionId));

    public ILocator HistoryCurrent => page.GetByTestId(WriteTestIds.HistoryCurrent);

    public ILocator HistoryAdded => page.GetByTestId(WriteTestIds.HistoryAdded);

    public ILocator HistoryRemoved => page.GetByTestId(WriteTestIds.HistoryRemoved);

    public ILocator HistoryDelta => page.GetByTestId(WriteTestIds.HistoryDelta);

    public ILocator HistoryRestore => page.GetByTestId(WriteTestIds.HistoryRestore);

    public ILocator HistoryConfirm => page.GetByTestId(WriteTestIds.HistoryConfirm);

    public ILocator HistoryConfirmText => page.GetByTestId(WriteTestIds.HistoryConfirmText);

    public ILocator HistoryCancel => page.GetByTestId(WriteTestIds.HistoryCancel);

    public ILocator HistoryUpdated => page.GetByTestId(WriteTestIds.HistoryUpdated);

    public ILocator HistoryReadOnly => page.GetByTestId(WriteTestIds.HistoryReadOnly);

    // Find and replace (L13.3): the toolbar's find box, the Replace panel and its matches.
    public ILocator Find => page.GetByTestId(WriteTestIds.Find);

    public ILocator FindCount => page.GetByTestId(WriteTestIds.FindCount);

    public ILocator ReplaceToggle => page.GetByTestId(WriteTestIds.ReplaceToggle);

    public ILocator ReplacePanel => page.GetByTestId(WriteTestIds.ReplacePanel);

    public ILocator ReplaceInput => page.GetByTestId(WriteTestIds.ReplaceInput);

    public ILocator ReplaceProse => page.GetByTestId(WriteTestIds.ReplaceProse);

    public ILocator ReplaceSummaries => page.GetByTestId(WriteTestIds.ReplaceSummaries);

    public ILocator ReplaceBeats => page.GetByTestId(WriteTestIds.ReplaceBeats);

    public ILocator ReplaceMatchCase => page.GetByTestId(WriteTestIds.ReplaceMatchCase);

    public ILocator ReplaceWholeWord => page.GetByTestId(WriteTestIds.ReplaceWholeWord);

    public ILocator ReplaceArchived => page.GetByTestId(WriteTestIds.ReplaceArchived);

    public ILocator ReplaceCount => page.GetByTestId(WriteTestIds.ReplaceCount);

    public ILocator ReplaceDrafts => page.GetByTestId(WriteTestIds.ReplaceDrafts);

    public ILocator ReplaceItems => page.GetByTestId(WriteTestIds.AnyReplaceItem());

    public ILocator ReplaceItem(int index) => page.GetByTestId(WriteTestIds.ReplaceItem(index));

    public ILocator ReplaceOne => page.GetByTestId(WriteTestIds.ReplaceOne);

    public ILocator ReplaceNext => page.GetByTestId(WriteTestIds.ReplaceNext);

    public ILocator ReplaceAll => page.GetByTestId(WriteTestIds.ReplaceAll);

    // Puts the caret at the start of the scene's first block (a paragraph). Not a click then Control+Home:
    // ProseMirror re-applies its own selection 20 ms after the editor gains focus, and under load that can land after
    // the keyboard has moved the caret but before ProseMirror has read the move, putting the caret back at the click.
    // So the click only focuses the editor (and tells ProseMirror the next selection is the user's, not a browser
    // reset after focus); the caret is then placed directly and ProseMirror reads it at once, so its timer finds
    // nothing to put back. A click at the start itself would land on the mention chip some scenes open with.
    public async Task PlaceCaretAtStartAsync(Guid sceneId)
    {
        await ProseEditor(sceneId).ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = 8 } });
        await ProseEditor(sceneId).EvaluateAsync(
            """
            element => {
                window.getSelection().collapse(element.firstElementChild, 0);
                document.dispatchEvent(new Event('selectionchange'));
            }
            """);
    }

    public ILocator Preview => page.GetByTestId(WriteTestIds.Preview);

    public ILocator PreviewCanvas => page.GetByTestId(WriteTestIds.PreviewCanvas);

    public ILocator PreviewPages => page.GetByTestId(WriteTestIds.PreviewPages);

    public ILocator PreviewTemplate => page.GetByTestId(WriteTestIds.PreviewTemplate);

    public ILocator PreviewInLayout => page.GetByTestId(WriteTestIds.PreviewInLayout);

    public ILocator PreviewClose => page.GetByTestId(WriteTestIds.PreviewClose);

    // Format → Preview: Show (or Hide), then closes the popover.
    public async Task ShowPreviewAsync(bool show = true)
    {
        await FormatButton.ClickAsync();
        await FormatOption("pane", show ? "show" : "hide").ClickAsync();
        await FormatButton.ClickAsync();
    }
}
