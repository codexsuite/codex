import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { router } from 'expo-router';
import { forgetDraft, readDraft } from '~/book/drafts';
import { bookSaveStatus } from '~/book/saveStatus';
import { sceneSheetLink } from '~/links';
import { queryClient } from '~/queryClient';
import { outlineKey } from '@codex/core/api/outline';
import { structureApi, type LabelRef, type Outline } from '@codex/core/api/structure';
import { bookHeader, kbEntry } from '~/test-support/fakes';
import { kbEntryKey } from '@codex/core/api/kbEntries';
import { renderApp } from '~/test-support/renderScreen';
import { shellTestIds } from '~/ui';
import { BOOK, planOutline, planRoutes } from './test-support/planWorld';
import { planTestIds as ids } from './testIds';

const PATCH_SCENE = 'PATCH /api/scenes/scene-1';
const PUT_LABELS = 'PUT /api/scenes/scene-1/labels';
const POST_LINK = 'POST /api/scenes/scene-1/kb';
const DELETE_LINK = 'DELETE /api/scenes/scene-1/kb/entry-1';

const patched = (body: unknown) => {
  const { title, summary } = body as { title: string | null; summary: string | null };
  const scene = planOutline().acts[0]!.chapters[0]!.scenes[0]!;
  return { ...scene, title: title ?? scene.title, summary: summary ?? scene.summary };
};

async function openSheet(url: string, api: Record<string, unknown> = {}) {
  return renderApp({ url, api: { ...planRoutes(), ...api } });
}

// Opens the sheet the way the app does, over a tab, never as the first screen of the stack.
async function openSheetOver(
  sceneId: string,
  api: Record<string, unknown> = {},
  tab: 'plan' | 'write' = 'plan',
) {
  const app = await openSheet(`/book/${BOOK}/${tab}`, api);
  await screen.findByTestId(`mobile-${tab}-screen`);
  await act(async () => router.push(sceneSheetLink(BOOK, sceneId)));
  return app;
}

async function openFromPlan(api: Record<string, unknown> = {}) {
  const app = await openSheet(`/book/${BOOK}/plan`, api);
  await fireEvent.press(await screen.findByTestId(ids.scene('scene-1')));
  await screen.findByTestId(ids.sheetTitle);
  return app;
}

const sceneTitle = () => screen.getByTestId(ids.sheetTitle);
const sceneSummary = () => screen.getByTestId(ids.sheetSummary);

describe('Scene sheet', () => {
  beforeEach(() => saveStatusStore.set('saved'));
  afterEach(() => forgetDraft(BOOK, 'plan:scene-1:summary'));

  it('Sheet_ShouldShowTheScenesFieldsLabelsEntriesAndWords', async () => {
    await openFromPlan();

    expect(screen.getByTestId(`${shellTestIds.sheet}-title`)).toHaveTextContent(
      /^Chapter 1 · Scene 1$/,
    );
    expect(sceneTitle()).toHaveDisplayValue('The Hall');
    expect(sceneSummary()).toHaveDisplayValue('Maren returns to the **Hollow Court**.');
    expect(screen.getByTestId(ids.sheetLabel('label-1'))).toHaveTextContent(/drafting/);
    expect(screen.getByTestId(ids.sheetLabel('label-2'))).toHaveTextContent(/odd/);
    expect(screen.getByTestId(ids.sheetEntry('entry-1'))).toHaveTextContent(/Ana/);
    expect(screen.getByTestId(ids.sheetNote)).toHaveTextContent(/3,000 words written/);
  });

  it('Sheet_WhenOpenedFromWrite_ShouldOpenOverWriteAndCloseBackToIt', async () => {
    const app = await openSheet(`/book/${BOOK}/write`);
    await screen.findByTestId('mobile-write-screen');

    await act(async () => router.push(sceneSheetLink(BOOK, 'scene-1')));

    expect(await screen.findByTestId(ids.sheetTitle)).toBeOnTheScreen();
    expect(app.getPathname()).toBe(`/book/${BOOK}/scene/scene-1`);
    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));
    await waitFor(() => expect(app.getPathname()).toBe(`/book/${BOOK}/write`));
  });

  it('Title_WhenEditedAndLeft_ShouldSaveOnBlurWithOnlyTheTitle', async () => {
    const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });

    await fireEvent.changeText(sceneTitle(), 'The Hall of Doors');
    expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(0);
    await fireEvent(sceneTitle(), 'blur');

    await waitFor(() => expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1));
    expect(app.api.callsTo(PATCH_SCENE)[0]?.body).toEqual({
      title: 'The Hall of Doors',
      summary: null,
      status: null,
    });
  });

  it('Summary_WhenEdited_ShouldAutosaveThreeSecondsAfterTheFirstEdit', async () => {
    const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });

    await fireEvent.changeText(sceneSummary(), 'Maren is late.');
    await act(async () => jest.advanceTimersByTime(2_900));
    expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(0);
    await act(async () => jest.advanceTimersByTime(200));

    await waitFor(() => expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1));
    expect(app.api.callsTo(PATCH_SCENE)[0]?.body).toEqual({
      title: null,
      summary: 'Maren is late.',
      status: null,
    });
  });

  it('Autosave_ShouldDriveTheBookBarsSaveStatusDot', async () => {
    const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });

    await fireEvent.changeText(sceneSummary(), 'Maren is late.');
    expect(saveStatusStore.getState()).toBe('unsaved');
    await fireEvent(sceneSummary(), 'blur');

    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
    expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1);
  });

  it('Autosave_WhenAWriteSceneCouldNotSave_ShouldNotTurnTheDotGreenWhenTheSummarySaves', async () => {
    const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });
    bookSaveStatus.forBook(BOOK).store('write:scene-2').set('retry');

    await fireEvent.changeText(sceneSummary(), 'Maren is late.');
    await fireEvent(sceneSummary(), 'blur');

    await waitFor(() => expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1));
    await act(async () => undefined);
    expect(saveStatusStore.getState()).toBe('retry');
  });

  it('Autosave_WhenTheSaveFailedAndTheSheetIsReopened_ShouldAdoptTheSameControllerAndClearTheDotWhenItSaves', async () => {
    let sent = 0;
    const app = await openFromPlan({
      [PATCH_SCENE]: (r: { body: unknown }) =>
        ++sent <= 2
          ? { status: 500, body: { error: 'Down.', code: 'internal_error' } }
          : patched(r.body),
    });
    await fireEvent.changeText(sceneSummary(), 'First try.');
    await fireEvent(sceneSummary(), 'blur');
    await waitFor(() => expect(saveStatusStore.getState()).toBe('retry'));
    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));
    await screen.findByTestId(ids.scene('scene-1'));

    await fireEvent.press(screen.getByTestId(ids.scene('scene-1')));
    await screen.findByTestId(ids.sheetSummary);

    // The sheet shows the text that was not saved, as an edit waiting to go.
    expect(sceneSummary().props.value).toBe('First try.');
    expect(saveStatusStore.getState()).toBe('unsaved');
    await fireEvent.changeText(sceneSummary(), 'Second try.');
    await fireEvent(sceneSummary(), 'blur');

    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
    expect(
      app.api.callsTo(PATCH_SCENE).map((c) => (c.body as { summary: string }).summary),
    ).toEqual(['First try.', 'First try.', 'Second try.']); // leaving the sheet sent it once more
    // Nothing is left retrying in the background.
    await act(async () => jest.advanceTimersByTime(120_000));
    expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(3);
  });

  it('Autosave_WhenTheSaveFailedAndNobodyReturns_ShouldRetryThenClearTheDot', async () => {
    const app = await openFromPlan({
      [PATCH_SCENE]: { status: 500, body: { error: 'Down.', code: 'internal_error' } },
    });
    await fireEvent.changeText(sceneSummary(), 'Lost cause.');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('retry'));

    for (const wait of [5_000, 15_000, 45_000]) {
      await act(async () => jest.advanceTimersByTime(wait));
    }

    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
    // Leaving sent it, then each retry; the text stays as a draft for the next time the sheet opens.
    expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(4);
    const serverSummary = planOutline().acts[0]!.chapters[0]!.scenes[0]!.summary ?? '';
    expect(readDraft(BOOK, 'plan:scene-1:summary', serverSummary)).toBe('Lost cause.');
  });

  it('Title_WhenSaved_ShouldUpdateTheCardOnPlanBehindTheSheet', async () => {
    await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });

    await fireEvent.changeText(sceneTitle(), 'The Hall of Doors');
    await fireEvent(sceneTitle(), 'blur');

    await waitFor(() =>
      expect(
        screen.getByTestId(ids.sceneTitle('scene-1'), { includeHiddenElements: true }),
      ).toHaveTextContent(/The Hall of Doors/),
    );
  });

  it('Title_WhenClearedAndSheetClosedBeforeTheDelay_ShouldStillSave', async () => {
    const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });

    await fireEvent.changeText(sceneTitle(), '');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));

    await waitFor(() => expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1));
    expect(app.api.callsTo(PATCH_SCENE)[0]?.body).toEqual({
      title: '',
      summary: null,
      status: null,
    });
  });

  it('Save_WhenTheServerRefuses_ShouldKeepTheTextAndMarkTheDotForRetry', async () => {
    await openFromPlan({
      [PATCH_SCENE]: { status: 500, body: { error: 'Down.', code: 'internal_error' } },
    });

    await fireEvent.changeText(sceneTitle(), 'Kept');
    await fireEvent(sceneTitle(), 'blur');

    await waitFor(() => expect(saveStatusStore.getState()).toBe('retry'));
    expect(sceneTitle()).toHaveDisplayValue('Kept');
  });

  it('Edit_OverWrite_ShouldUpdateTheCachedOutlineWriteReads', async () => {
    await openSheetOver(
      'scene-1',
      { [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) },
      'write',
    );
    await screen.findByTestId(ids.sheetTitle);

    await fireEvent.changeText(sceneTitle(), 'Over Write');
    await fireEvent(sceneTitle(), 'blur');

    await waitFor(() => {
      const cached = queryClient.getQueryData<Outline>(outlineKey(BOOK));
      expect(cached?.acts[0]?.chapters[0]?.scenes[0]?.title).toBe('Over Write');
    });
  });

  it('Save_WhenALabelsChangeLandedMeanwhile_ShouldOnlyCopyBackTheSavedField', async () => {
    await openFromPlan({
      [PATCH_SCENE]: (r: { body: unknown }) => ({ ...patched(r.body), labels: [] }),
    });

    await fireEvent.changeText(sceneTitle(), 'Retitled');
    await fireEvent(sceneTitle(), 'blur');

    await waitFor(() => {
      const cached = queryClient.getQueryData<Outline>(outlineKey(BOOK));
      expect(cached?.acts[0]?.chapters[0]?.scenes[0]).toMatchObject({ title: 'Retitled' });
    });
    expect(screen.getByTestId(ids.sheetLabel('label-1'))).toBeOnTheScreen();
  });

  it('Save_AfterTheSheetClosedAndTheServerRefuses_ShouldShowOnTheDot', async () => {
    await openFromPlan({
      [PATCH_SCENE]: { status: 500, body: { error: 'Down.', code: 'internal_error' } },
    });
    await fireEvent.changeText(sceneTitle(), 'Lost?');

    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));

    await waitFor(() => expect(saveStatusStore.getState()).toBe('retry'));
  });

  describe('labels', () => {
    it('Picker_ShouldOfferTheBooksLabelsWithTheSceneOnesSelected', async () => {
      await openFromPlan();

      await fireEvent.press(screen.getByTestId(ids.sheetLabelAdd));

      expect(await screen.findByTestId(ids.sheetLabelOption('label-3'))).toHaveTextContent(
        /revise/,
      );
      expect(screen.getByTestId(ids.sheetLabelOption('label-1'))).toBeSelected();
      expect(screen.getByTestId(ids.sheetLabelOption('label-3'))).not.toBeSelected();
    });

    it('Picker_WhenALabelIsPicked_ShouldSaveTheWholeSelectionAtOnce', async () => {
      const app = await openFromPlan({
        [PUT_LABELS]: [
          { id: 'label-1', name: 'drafting', color: 'info' },
          { id: 'label-2', name: 'odd', color: '#abcdef' },
          { id: 'label-3', name: 'revise', color: 'warn' },
        ],
      });
      await fireEvent.press(screen.getByTestId(ids.sheetLabelAdd));

      await fireEvent.press(await screen.findByTestId(ids.sheetLabelOption('label-3')));

      await waitFor(() => expect(app.api.callsTo(PUT_LABELS)).toHaveLength(1));
      expect(app.api.callsTo(PUT_LABELS)[0]?.body).toEqual({
        labelIds: ['label-1', 'label-2', 'label-3'],
      });
      expect(await screen.findByTestId(ids.sheetLabel('label-3'))).toBeOnTheScreen();
    });

    it('Chip_WhenPressed_ShouldTakeTheLabelOffTheScene', async () => {
      const app = await openFromPlan({
        [PUT_LABELS]: [{ id: 'label-2', name: 'odd', color: '#abcdef' }],
      });

      await fireEvent.press(screen.getByTestId(ids.sheetLabel('label-1')));

      await waitFor(() => expect(app.api.callsTo(PUT_LABELS)).toHaveLength(1));
      expect(app.api.callsTo(PUT_LABELS)[0]?.body).toEqual({ labelIds: ['label-2'] });
      await waitFor(() => expect(screen.queryByTestId(ids.sheetLabel('label-1'))).toBeNull());
    });

    it('QuickTaps_BeforeTheFirstSaveAnswers_ShouldBuildOnTheNewSelection', async () => {
      const answers: ((labels: LabelRef[]) => void)[] = [];
      const put = jest
        .spyOn(structureApi, 'setSceneLabels')
        .mockImplementation(() => new Promise((resolve) => answers.push(resolve)));
      await openFromPlan();
      await fireEvent.press(screen.getByTestId(ids.sheetLabelAdd));

      await fireEvent.press(await screen.findByTestId(ids.sheetLabelOption('label-3')));
      await fireEvent.press(screen.getByTestId(ids.sheetLabel('label-1')));

      expect(put.mock.calls.map(([, body]) => body.labelIds)).toEqual([
        ['label-1', 'label-2', 'label-3'],
        ['label-2', 'label-3'],
      ]);
      await act(async () => {
        answers[0]?.([{ id: 'label-1', name: 'drafting', color: 'info' }]);
        answers[1]?.([
          { id: 'label-2', name: 'odd', color: '#abcdef' },
          { id: 'label-3', name: 'revise', color: 'warn' },
        ]);
      });
      // The older answer does not undo the newer tap.
      await waitFor(() => expect(screen.queryByTestId(ids.sheetLabel('label-1'))).toBeNull());
      expect(screen.getByTestId(ids.sheetLabel('label-3'))).toBeOnTheScreen();
    });

    it('NewLabel_ShouldBeCreatedWithTheDesktopColourAndAddedToTheScene', async () => {
      const app = await openFromPlan({
        [`POST /api/books/${BOOK}/labels`]: {
          id: 'label-9',
          scopeId: BOOK,
          name: 'cut?',
          color: 'info',
          sceneCount: 0,
        },
        [PUT_LABELS]: [{ id: 'label-9', name: 'cut?', color: 'info' }],
      });
      await fireEvent.press(screen.getByTestId(ids.sheetLabelAdd));

      await fireEvent.changeText(await screen.findByTestId(ids.sheetLabelNew), '  cut?  ');
      await fireEvent.press(screen.getByTestId(ids.sheetLabelCreate));

      await waitFor(() => expect(app.api.callsTo(PUT_LABELS)).toHaveLength(1));
      expect(app.api.callsTo(`POST /api/books/${BOOK}/labels`)[0]?.body).toEqual({
        name: 'cut?',
        color: 'info',
      });
      expect(app.api.callsTo(PUT_LABELS)[0]?.body).toEqual({
        labelIds: ['label-1', 'label-2', 'label-9'],
      });
    });

    it('Save_WhenItFails_ShouldSayWhyAndLeaveTheLabelsAlone', async () => {
      await openFromPlan({
        [PUT_LABELS]: { status: 403, body: { error: 'Read only.', code: 'forbidden' } },
      });

      await fireEvent.press(screen.getByTestId(ids.sheetLabel('label-1')));

      expect(await screen.findByTestId(`${ids.sheetError}-labels`)).toHaveTextContent(
        /Read only\./,
      );
      expect(screen.getByTestId(ids.sheetLabel('label-1'))).toBeOnTheScreen();
    });
  });

  describe('entries', () => {
    it('Picker_ShouldSearchTheBooksEntriesAndLeaveOutLinkedOnes', async () => {
      await openFromPlan();
      await fireEvent.press(screen.getByTestId(ids.sheetEntryAdd));

      expect(await screen.findByTestId(ids.sheetEntryOption('entry-2'))).toHaveTextContent(/Tobin/);
      expect(screen.queryByTestId(ids.sheetEntryOption('entry-1'))).toBeNull();

      await fireEvent.changeText(screen.getByTestId(ids.sheetEntrySearch), 'zzz');
      expect(screen.getByTestId(ids.sheetEntryNone)).toBeOnTheScreen();
      await fireEvent.changeText(screen.getByTestId(ids.sheetEntrySearch), 'counter');
      expect(screen.getByTestId(ids.sheetEntryOption('entry-2'))).toBeOnTheScreen();
    });

    it('Picker_WhenAnEntryIsPicked_ShouldLinkItAndShowItsChip', async () => {
      const app = await openFromPlan({ [POST_LINK]: { status: 204 } });
      await fireEvent.press(screen.getByTestId(ids.sheetEntryAdd));

      await fireEvent.press(await screen.findByTestId(ids.sheetEntryOption('entry-2')));

      await waitFor(() => expect(app.api.callsTo(POST_LINK)).toHaveLength(1));
      expect(app.api.callsTo(POST_LINK)[0]?.body).toEqual({ entryId: 'entry-2' });
      expect(await screen.findByTestId(ids.sheetEntry('entry-2'))).toHaveTextContent(/Tobin/);
    });

    it('Chip_WhenPressed_ShouldUnlinkTheEntry', async () => {
      const app = await openFromPlan({ [DELETE_LINK]: { status: 204 } });

      await fireEvent.press(screen.getByTestId(ids.sheetEntry('entry-1')));

      await waitFor(() => expect(app.api.callsTo(DELETE_LINK)).toHaveLength(1));
      await waitFor(() => expect(screen.queryByTestId(ids.sheetEntry('entry-1'))).toBeNull());
    });

    it('LinkAndUnlink_ShouldReloadTheEntriesOwnScreenData', async () => {
      const app = await openFromPlan({
        [POST_LINK]: { status: 204 },
        [DELETE_LINK]: { status: 204 },
        'GET /api/kb/entries/entry-2': kbEntry({ id: 'entry-2' }),
        'GET /api/kb/entries/entry-1': kbEntry({ id: 'entry-1' }),
      });
      queryClient.setQueryData(kbEntryKey('entry-2'), kbEntry({ id: 'entry-2' }));
      queryClient.setQueryData(kbEntryKey('entry-1'), kbEntry({ id: 'entry-1' }));
      await fireEvent.press(screen.getByTestId(ids.sheetEntryAdd));
      await fireEvent.press(await screen.findByTestId(ids.sheetEntryOption('entry-2')));
      await fireEvent.press(await screen.findByTestId(ids.sheetEntry('entry-1')));

      await waitFor(() => {
        expect(queryClient.getQueryState(kbEntryKey('entry-2'))?.isInvalidated).toBe(true);
        expect(queryClient.getQueryState(kbEntryKey('entry-1'))?.isInvalidated).toBe(true);
      });
      expect(app.api.callsTo(POST_LINK)).toHaveLength(1);
    });

    it('Link_WhenItFails_ShouldSayWhyAndNotShowTheChip', async () => {
      await openFromPlan({
        [POST_LINK]: { status: 404, body: { error: 'No such entry.', code: 'not_found' } },
      });
      await fireEvent.press(screen.getByTestId(ids.sheetEntryAdd));

      await fireEvent.press(await screen.findByTestId(ids.sheetEntryOption('entry-2')));

      expect(await screen.findByTestId(`${ids.sheetError}-entries`)).toHaveTextContent(
        /No such entry\./,
      );
      expect(screen.queryByTestId(ids.sheetEntry('entry-2'))).toBeNull();
    });
  });

  describe('dock', () => {
    it('Record_ShouldOpenTheRecordingSheetForThisScene', async () => {
      const app = await openFromPlan();

      await fireEvent.press(screen.getByTestId(ids.sheetRecord));

      expect(await screen.findByTestId('mobile-record-sheet')).toBeOnTheScreen();
      expect(app.getPathname()).toBe('/record');
      expect(app.getSearchParams()).toEqual({ bookId: BOOK, target: 'scene:scene-1' });
    });

    it('OpenInWrite_ShouldCloseTheSheetAndShowTheSceneInWrite', async () => {
      const app = await openFromPlan();

      await fireEvent.press(screen.getByTestId(ids.sheetOpenInWrite));

      expect(await screen.findByTestId('mobile-write-screen')).toBeOnTheScreen();
      expect(app.getPathname()).toBe(`/book/${BOOK}/write`);
      expect(app.getSearchParams()).toEqual({ bookId: BOOK, sceneId: 'scene-1' });
      expect(screen.queryByTestId(ids.sheetTitle)).toBeNull();
      expect(screen.getByTestId(shellTestIds.tab('write'))).toBeSelected();
    });

    it('OpenInWrite_FromOverWrite_ShouldKeepOneWriteScreenWithTheScene', async () => {
      const app = await openSheetOver('scene-2', {}, 'write');
      await screen.findByTestId(ids.sheetOpenInWrite);

      await fireEvent.press(screen.getByTestId(ids.sheetOpenInWrite));

      await waitFor(() => expect(app.getPathname()).toBe(`/book/${BOOK}/write`));
      expect(app.getSearchParams()).toEqual({ bookId: BOOK, sceneId: 'scene-2' });
      expect(screen.queryByTestId(ids.sheetTitle)).toBeNull();
      expect(screen.getAllByTestId('mobile-write-screen')).toHaveLength(1);
    });

    it('Dock_ShouldPutOpenInWriteOnTheLeftAndRecordAsThePrimaryOnTheRight', async () => {
      await openFromPlan();

      const dock = screen.getByTestId(`${shellTestIds.sheet}-dock`);
      const labels = within(dock)
        .getAllByRole('button')
        .map((b) => b.props.testID);
      expect(labels).toEqual([ids.sheetOpenInWrite, ids.sheetRecord]);
    });

    it('OpenInWrite_WithAnEditWaiting_ShouldSaveItBeforeLeaving', async () => {
      const app = await openFromPlan({ [PATCH_SCENE]: (r: { body: unknown }) => patched(r.body) });
      await fireEvent.changeText(sceneSummary(), 'Quick note.');

      await fireEvent.press(screen.getByTestId(ids.sheetOpenInWrite));

      await waitFor(() => expect(app.api.callsTo(PATCH_SCENE)).toHaveLength(1));
    });
  });

  describe('states', () => {
    it('ReadOnlyRole_ShouldShowTheFieldsButNotEditThem', async () => {
      const app = await openFromPlan({
        [`GET /api/books/${BOOK}`]: bookHeader({ id: BOOK, role: 'view' }),
      });

      await waitFor(() => expect(sceneTitle()).toHaveProp('editable', false));
      expect(sceneSummary()).toHaveProp('editable', false);
      expect(screen.queryByTestId(ids.sheetLabelAdd)).toBeNull();
      expect(screen.queryByTestId(ids.sheetEntryAdd)).toBeNull();
      await userEvent.press(screen.getByTestId(ids.sheetLabel('label-1')));
      expect(app.api.callsTo(PUT_LABELS)).toHaveLength(0);
      expect(screen.getByTestId(ids.sheetRecord)).toBeDisabled();
      expect(screen.getByTestId(ids.sheetOpenInWrite)).toBeEnabled();
      expect(screen.getByTestId(ids.sheetNote)).not.toHaveTextContent(/syncs/);
    });

    it('Scene_WhenNotInTheOutline_ShouldSayItIsMissing', async () => {
      await openSheetOver('scene-nope');

      expect(await screen.findByTestId(ids.sheetMissing)).toBeOnTheScreen();
    });

    it('Scene_OfAnArchivedChapter_ShouldSayItIsMissing', async () => {
      await openSheetOver('scene-old');

      expect(await screen.findByTestId(ids.sheetMissing)).toBeOnTheScreen();
    });

    it('Outline_WhileLoading_ShouldShowASpinnerInTheSheet', async () => {
      await openSheet(`/book/${BOOK}/scene/scene-1`, {
        [`GET /api/books/${BOOK}/outline`]: () => new Promise(() => undefined),
      });

      expect(await screen.findByTestId(ids.sheetLoading)).toBeOnTheScreen();
    });

    it('Outline_WhenItFailsToLoad_ShouldShowAnErrorWithRetry', async () => {
      await openSheet(`/book/${BOOK}/scene/scene-1`, {
        [`GET /api/books/${BOOK}/outline`]: {
          status: 500,
          body: { error: 'The outline is down.', code: 'internal_error' },
        },
      });

      expect(await screen.findByTestId(ids.sheetError)).toHaveTextContent(/The outline is down\./);
      expect(screen.getByTestId(`${ids.sheetError}-retry`)).toBeOnTheScreen();
    });

    it('Scene_ForAnUnnumberedChapter_ShouldHeadTheSheetWithJustTheSceneNumber', async () => {
      await openSheet(`/book/${BOOK}/scene/scene-1`, {
        [`GET /api/books/${BOOK}/outline`]: (() => {
          const o = planOutline();
          o.acts[0]!.chapters[0]!.scenes[0]!.number = '4';
          return o;
        })(),
      });

      await screen.findByTestId(ids.sheetTitle);
      expect(screen.getByTestId(`${shellTestIds.sheet}-title`)).toHaveTextContent(/^Scene 4$/);
    });
  });
});
