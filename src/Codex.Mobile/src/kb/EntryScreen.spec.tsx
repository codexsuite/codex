import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { ScrollView } from 'react-native';
import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { forgetDraft, keepDraft, readDraft } from '~/book/drafts';
import { router } from 'expo-router';
import { entryLink } from '~/links';
import { mobilePlatform } from '~/mobilePlatform';
import { queryClient } from '~/queryClient';
import { kbEntryKey } from '@codex/core/api/kbEntries';
import { bookHeader } from '~/test-support/fakes';
import { lightTheme } from '~/theme';
import { renderApp } from '~/test-support/renderScreen';
import { marenAdditions, marenEntry, seriesKb } from './test-support/kbWorld';
import { kbTestIds } from './testIds';

const ENTRY = 'GET /api/kb/entries/maren';
const PATCH = 'PATCH /api/kb/entries/maren';
const baseApi = {
  'GET /api/books/book-1/kb': seriesKb,
  [ENTRY]: marenEntry,
  'GET /api/books/book-1/kb-additions': marenAdditions,
};
const open = (api: Record<string, unknown> = {}) =>
  renderApp({ url: '/book/book-1/knowledge/maren', api: { ...baseApi, ...api } });
const patchBodies = (app: Awaited<ReturnType<typeof open>>) =>
  app.api.callsTo(PATCH).map((c) => c.body);

afterEach(() => forgetDraft('book-1', 'kb:maren'));

describe('Entry screen', () => {
  it('Entry_WhenLoaded_ShouldShowCategoryNameAliasesAndSummary', async () => {
    await open();

    expect(await screen.findByTestId(kbTestIds.entryName)).toHaveTextContent('Maren');
    expect(screen.getByTestId(kbTestIds.entryCategory)).toHaveTextContent('Characters');
    expect(screen.getByTestId(kbTestIds.alias('The Queen'))).toBeOnTheScreen();
    expect(screen.getByTestId(kbTestIds.summary).props.value).toBe('Rules from the north.');
    expect(screen.getByTestId(kbTestIds.always)).toBeOnTheScreen();
    expect(screen.queryByText(/voice notes/i)).toBeNull();
  });

  it('Entry_UsedIn_ShouldListThisBooksScenesOnlyAndOpenTheSceneSheet', async () => {
    const app = await open();
    await screen.findByTestId(kbTestIds.entryName);

    expect(screen.getByTestId(kbTestIds.usedInRow('scene-1'))).toHaveTextContent(
      /^Chapter 1 · Scene 1/,
    );
    expect(screen.getByTestId(kbTestIds.usedInRow('scene-1'))).toHaveTextContent(
      /Maren does not come to the hall\./,
    );
    expect(screen.queryByTestId(kbTestIds.usedInRow('scene-9'))).toBeNull();

    await fireEvent.press(screen.getByTestId(kbTestIds.usedInRow('scene-1')));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/scene/scene-1'));
  });

  it('Entry_Additions_ShouldShowOnlyThisEntrysLabelledByScene', async () => {
    await open();

    expect(await screen.findByTestId(kbTestIds.addition('add-1'))).toHaveTextContent(
      'Chapter 1 · Scene 1Lies with a level voice.',
    );
    expect(screen.queryByTestId(kbTestIds.addition('add-2'))).toBeNull();
  });

  it('Entry_WhenAlwaysInContextIsSwitched_ShouldPatchItAtOnce', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, alwaysInContext: true } });
    await screen.findByTestId(kbTestIds.entryName);

    await fireEvent(screen.getByTestId(`${kbTestIds.always}-switch`), 'valueChange', true);

    await waitFor(() => expect(patchBodies(app)).toEqual([{ alwaysInContext: true }]));
    await waitFor(() =>
      expect(screen.getByTestId(`${kbTestIds.always}-switch`).props.value).toBe(true),
    );
  });

  it('Entry_WhenAlwaysInContextFailsToSave_ShouldSwitchBackAndSayWhy', async () => {
    await open({
      [PATCH]: { status: 403, body: { error: 'Read-only access.', code: 'forbidden' } },
    });
    await screen.findByTestId(kbTestIds.entryName);

    await fireEvent(screen.getByTestId(`${kbTestIds.always}-switch`), 'valueChange', true);

    expect(await screen.findByTestId(kbTestIds.changeError)).toHaveTextContent('Read-only access.');
    expect(screen.getByTestId(`${kbTestIds.always}-switch`).props.value).toBe(false);
  });

  it('Entry_WhenTheSummaryIsLeft_ShouldSaveItOnBlur', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, summary: 'New text.' } });
    const summary = await screen.findByTestId(kbTestIds.summary);

    await fireEvent.changeText(summary, 'New text.');
    expect(patchBodies(app)).toEqual([]);
    await fireEvent(summary, 'blur');

    await waitFor(() => expect(patchBodies(app)).toEqual([{ summary: 'New text.' }]));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
  });

  it('Entry_WhenTheSummaryIsEdited_ShouldSaveAfterThreeSecondsOfQuiet', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, summary: 'Typed.' } });
    await fireEvent.changeText(await screen.findByTestId(kbTestIds.summary), 'Typed.');
    expect(saveStatusStore.getState()).toBe('unsaved');

    await act(async () => {
      jest.advanceTimersByTime(3000);
    });

    await waitFor(() => expect(patchBodies(app)).toEqual([{ summary: 'Typed.' }]));
  });

  it('Entry_WhenAnAliasIsAdded_ShouldPatchTheWholeSetAndTrimIt', async () => {
    const app = await open({
      [PATCH]: { ...marenEntry, aliases: ['The Queen', 'Her Grace'] },
      'GET /api/kb/entries/maren/alias-check': { collidesWith: null, matches: 1, warn: false },
    });
    await fireEvent.press(await screen.findByTestId(kbTestIds.aliasAdd));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.aliasInput), '  Her Grace ');
    await fireEvent(screen.getByTestId(kbTestIds.aliasInput), 'submitEditing');

    await waitFor(() =>
      expect(patchBodies(app)).toEqual([{ aliases: ['The Queen', 'Her Grace'] }]),
    );
    expect(await screen.findByTestId(kbTestIds.alias('Her Grace'))).toBeOnTheScreen();
  });

  it('Entry_WhenAShortAliasIsAdded_ShouldWarnHowManyWordsItMatches', async () => {
    await open({
      [PATCH]: { ...marenEntry, aliases: ['The Queen', 'Ma'] },
      'GET /api/kb/entries/maren/alias-check': { collidesWith: null, matches: 40, warn: true },
    });
    await fireEvent.press(await screen.findByTestId(kbTestIds.aliasAdd));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.aliasInput), 'Ma');
    await fireEvent(screen.getByTestId(kbTestIds.aliasInput), 'submitEditing');

    expect(await screen.findByTestId(kbTestIds.aliasWarning)).toHaveTextContent(
      'Matches 40 other words in the book. Underlines every "Ma".',
    );
  });

  it('Entry_WhenAnAliasBelongsToAnotherEntry_ShouldShowTheMessageAndAddAnywayResendsIt', async () => {
    const app = await open({
      [PATCH]: (request: { body: unknown }) =>
        (request.body as { allowDuplicate?: boolean }).allowDuplicate
          ? { ...marenEntry, aliases: ['The Queen', 'Tobin'] }
          : { status: 409, body: { error: 'Tobin already answers to Tobin.', code: 'conflict' } },
      'GET /api/kb/entries/maren/alias-check': { collidesWith: null, matches: 1, warn: false },
    });
    await fireEvent.press(await screen.findByTestId(kbTestIds.aliasAdd));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.aliasInput), 'Tobin');
    await fireEvent(screen.getByTestId(kbTestIds.aliasInput), 'submitEditing');

    expect(await screen.findByTestId(kbTestIds.aliasError)).toHaveTextContent(
      'Tobin already answers to Tobin.',
    );

    await fireEvent.press(screen.getByTestId(kbTestIds.aliasAddAnyway));

    await waitFor(() =>
      expect(patchBodies(app)[1]).toEqual({
        aliases: ['The Queen', 'Tobin'],
        allowDuplicate: true,
      }),
    );
  });

  it('Entry_WhenAnAliasIsRemoved_ShouldPatchTheRest', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, aliases: [] } });
    await fireEvent.press(await screen.findByTestId(kbTestIds.aliasRemove('The Queen')));

    await waitFor(() => expect(patchBodies(app)).toEqual([{ aliases: [] }]));
  });

  it('Entry_RecordToEntry_ShouldOpenTheRecordSheetForThisEntry', async () => {
    const app = await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.dockRecord));

    await waitFor(() => expect(app.getPathname()).toBe('/record'));
    expect(app.getSearchParams()).toEqual({ bookId: 'book-1', target: 'entry:maren' });
  });

  it('Entry_ScenesInTheDock_ShouldScrollToUsedIn', async () => {
    await open();
    await screen.findByTestId(kbTestIds.entryName);
    await fireEvent(screen.getByTestId(kbTestIds.usedIn), 'layout', {
      nativeEvent: { layout: { x: 0, y: 420, width: 390, height: 200 } },
    });
    const scrollTo = jest.spyOn(ScrollView.prototype, 'scrollTo');

    await fireEvent.press(screen.getByTestId(kbTestIds.dockScenes));

    expect(scrollTo).toHaveBeenCalledWith({ y: 420, animated: true });
  });

  it('Entry_WhenTheEntryIsGone_ShouldSayNotFound', async () => {
    await open({ [ENTRY]: { status: 404, body: { error: 'No such entry.', code: 'not_found' } } });

    expect(await screen.findByTestId(kbTestIds.entryNotFound)).toBeOnTheScreen();
  });

  it('Entry_WhenLoadingFails_ShouldShowTheErrorAndRetryLoadsIt', async () => {
    let fail = true;
    await open({
      [ENTRY]: () =>
        fail ? { status: 500, body: { error: 'Boom', code: 'internal_error' } } : marenEntry,
    });

    expect(await screen.findByTestId(kbTestIds.entryError)).toHaveTextContent('Boom');

    fail = false;
    await fireEvent.press(screen.getByTestId(`${kbTestIds.entryError}-retry`));

    expect(await screen.findByTestId(kbTestIds.entryName)).toHaveTextContent('Maren');
  });

  it('Entry_WhenTheEntryIsRefetchedWithANewSummary_ShouldShowItAndTheNextEditBuildsOnIt', async () => {
    let served = marenEntry;
    const app = await open({ [ENTRY]: () => served, [PATCH]: marenEntry });
    const summary = await screen.findByTestId(kbTestIds.summary);

    // A recording appended a transcript to the summary and invalidated the entry.
    served = { ...marenEntry, summary: 'Rules from the north.\n\nTranscript.' };
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: kbEntryKey('maren') });
    });

    await waitFor(() => expect(summary.props.value).toBe('Rules from the north.\n\nTranscript.'));
    expect(saveStatusStore.getState()).toBe('saved');

    await fireEvent.changeText(summary, 'Rules from the north.\n\nTranscript. More.');
    await fireEvent(summary, 'blur');

    await waitFor(() =>
      expect(patchBodies(app)).toEqual([{ summary: 'Rules from the north.\n\nTranscript. More.' }]),
    );
  });

  it('Entry_WhenTheFieldAdoptedNewerServerTextAndTheEditThenGaveUp_ShouldRestoreTheDraftOnTheNextOpen', async () => {
    let served = marenEntry;
    const down = { status: 500, body: { error: 'Down.', code: 'internal_error' } };
    await open({ [ENTRY]: () => served, [PATCH]: down });
    const summary = await screen.findByTestId(kbTestIds.summary);
    served = { ...marenEntry, summary: 'Rules from the north.\n\nTranscript.' };
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: kbEntryKey('maren') });
    });
    await waitFor(() => expect(summary.props.value).toBe('Rules from the north.\n\nTranscript.'));
    await fireEvent.changeText(summary, 'Rules from the north.\n\nTranscript. More.');

    // Leave: the save is refused, tried again after 5, 15 and 45 s, then given up with the text kept as a draft.
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('retry'));
    for (const wait of [5_000, 15_000, 45_000]) {
      await act(async () => {
        jest.advanceTimersByTime(wait);
      });
    }
    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));

    await act(async () => router.push(entryLink('book-1', 'maren')));

    expect((await screen.findByTestId(kbTestIds.summary)).props.value).toBe(
      'Rules from the north.\n\nTranscript. More.',
    );
  });

  it('Entry_WhenTheEntryIsRefetchedWhileTheSummaryHasAnUnsavedEdit_ShouldKeepTheEdit', async () => {
    let served = marenEntry;
    await open({ [ENTRY]: () => served });
    const summary = await screen.findByTestId(kbTestIds.summary);
    await fireEvent.changeText(summary, 'Typing…');

    served = { ...marenEntry, summary: 'From the server.' };
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: kbEntryKey('maren') });
    });

    expect(summary.props.value).toBe('Typing…');
  });

  it('Entry_WhenAlwaysInContextChangesOnTheServer_ShouldShowIt', async () => {
    let served = marenEntry;
    await open({ [ENTRY]: () => served });
    await screen.findByTestId(kbTestIds.entryName);

    served = { ...marenEntry, alwaysInContext: true };
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: kbEntryKey('maren') });
    });

    await waitFor(() =>
      expect(screen.getByTestId(`${kbTestIds.always}-switch`).props.value).toBe(true),
    );
  });

  it('Entry_WhenTheSummarySaveIsForbidden_ShouldPutTheSavedTextBack', async () => {
    await open({
      [PATCH]: { status: 403, body: { error: 'Read-only access.', code: 'forbidden' } },
    });
    const summary = await screen.findByTestId(kbTestIds.summary);

    await fireEvent.changeText(summary, 'Not allowed.');
    await fireEvent(summary, 'blur');

    await waitFor(() => expect(summary.props.value).toBe('Rules from the north.'));
  });

  it('Entry_WhenTheScreenIsLeftWithAnUnsavedSummary_ShouldSendThePatch', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, summary: 'Left behind.' } });
    await fireEvent.changeText(await screen.findByTestId(kbTestIds.summary), 'Left behind.');
    expect(patchBodies(app)).toEqual([]);

    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));

    await waitFor(() => expect(patchBodies(app)).toEqual([{ summary: 'Left behind.' }]));
  });

  it('Entry_WhenTheScreenIsLeftOffline_ShouldKeepTheEditAndSendItWhenTheConnectionReturns', async () => {
    const app = await open();
    const summary = await screen.findByTestId(kbTestIds.summary);
    let backOnline: () => void = () => undefined;
    jest.spyOn(mobilePlatform, 'isOffline').mockReturnValue(true);
    jest.spyOn(mobilePlatform, 'onOnline').mockImplementation((listener) => {
      backOnline = listener;
      return () => undefined;
    });
    const original = globalThis.fetch;
    globalThis.fetch = jest.fn((input: Parameters<typeof fetch>[0], init?: RequestInit) =>
      init?.method === 'PATCH'
        ? Promise.reject(new TypeError('Network request failed'))
        : original(input, init),
    ) as unknown as typeof fetch;

    await fireEvent.changeText(summary, 'Written on the train.');
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));
    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/knowledge'));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('offline'));

    globalThis.fetch = original;
    app.api.respond(PATCH, { ...marenEntry, summary: 'Written on the train.' });
    jest.mocked(mobilePlatform.isOffline).mockReturnValue(false);
    await act(async () => {
      backOnline();
    });

    await waitFor(() => expect(patchBodies(app)).toEqual([{ summary: 'Written on the train.' }]));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
  });

  it('Entry_RecordToEntry_WithATypedSummary_ShouldSaveItBeforeOpeningTheRecordSheet', async () => {
    const app = await open({ [PATCH]: { ...marenEntry, summary: 'Typed first.' } });
    await fireEvent.changeText(await screen.findByTestId(kbTestIds.summary), 'Typed first.');

    await fireEvent.press(screen.getByTestId(kbTestIds.dockRecord));

    await waitFor(() => expect(app.getPathname()).toBe('/record'));
    expect(patchBodies(app)).toEqual([{ summary: 'Typed first.' }]);
  });

  it('Entry_RecordToEntry_WhenTheSummaryCannotBeSaved_ShouldSaySoAndNotOpenTheRecordSheet', async () => {
    const app = await open();
    jest.spyOn(mobilePlatform, 'isOffline').mockReturnValue(true);
    jest.spyOn(mobilePlatform, 'onOnline').mockReturnValue(() => undefined);
    const original = globalThis.fetch;
    globalThis.fetch = jest.fn((input: Parameters<typeof fetch>[0], init?: RequestInit) =>
      init?.method === 'PATCH'
        ? Promise.reject(new TypeError('Network request failed'))
        : original(input, init),
    ) as unknown as typeof fetch;
    await fireEvent.changeText(await screen.findByTestId(kbTestIds.summary), 'Not on the server.');

    await fireEvent.press(screen.getByTestId(kbTestIds.dockRecord));

    expect(await screen.findByTestId(kbTestIds.summaryNotSaved)).toHaveTextContent(
      /^Summary not saved yet\./,
    );
    expect(app.getPathname()).toBe('/book/book-1/knowledge/maren');
    globalThis.fetch = original;
  });

  it('Entry_WithADraftBasedOnTheServersSummary_ShouldShowItSaveItOnLeavingAndForgetIt', async () => {
    keepDraft('book-1', 'kb:maren', 'Draft text.', 'Rules from the north.');
    const app = await open({ [PATCH]: { ...marenEntry, summary: 'Draft text.' } });

    expect((await screen.findByTestId(kbTestIds.summary)).props.value).toBe('Draft text.');
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));

    await waitFor(() => expect(patchBodies(app)).toEqual([{ summary: 'Draft text.' }]));
    await waitFor(() =>
      expect(readDraft('book-1', 'kb:maren', 'Rules from the north.')).toBeUndefined(),
    );
  });

  it('Entry_WithADraftBasedOnOlderServerText_ShouldShowTheServersSummaryAndNeverSendTheDraft', async () => {
    // A recording appended a transcript after the draft was kept: the server's text is newer than the draft's base.
    keepDraft('book-1', 'kb:maren', 'Old draft.', 'Rules from the north. (before the transcript)');
    const app = await open();

    expect((await screen.findByTestId(kbTestIds.summary)).props.value).toBe(
      'Rules from the north.',
    );
    await fireEvent.press(screen.getByTestId('mobile-shell-push-bar-back'));
    await act(async () => {
      jest.advanceTimersByTime(60_000);
    });

    expect(patchBodies(app)).toEqual([]);
    expect(
      readDraft('book-1', 'kb:maren', 'Rules from the north. (before the transcript)'),
    ).toBeUndefined();
  });

  it('Entry_AdditionCard_ShouldBorderInTheEntrysCategoryColour', async () => {
    await open();

    const card = await screen.findByTestId(kbTestIds.addition('add-1'));

    expect(card).toHaveStyle({ borderLeftColor: lightTheme.category.person });
  });
});

describe('Entry screen: aliases', () => {
  const slow = (aliases: string[]) => ({
    [PATCH]: async () => ({ ...marenEntry, aliases }),
    'GET /api/kb/entries/maren/alias-check': { collidesWith: null, matches: 1, warn: false },
  });

  it('Entry_WhenAnAliasIsSubmittedAndThenBlurred_ShouldAddItOnce', async () => {
    const app = await open(slow(['The Queen', 'Her Grace']));
    await fireEvent.press(await screen.findByTestId(kbTestIds.aliasAdd));
    const input = screen.getByTestId(kbTestIds.aliasInput);
    await fireEvent.changeText(input, 'Her Grace');

    // Both land before the first request does.
    await act(async () => {
      input.props.onSubmitEditing();
      input.props.onBlur();
    });

    await waitFor(() => expect(screen.queryByTestId(kbTestIds.aliasInput)).toBeNull());
    expect(patchBodies(app)).toEqual([{ aliases: ['The Queen', 'Her Grace'] }]);
  });

  it('Entry_WhenTwoRemovesComeBeforeTheFirstLands_ShouldNotBuildTheSecondFromStaleAliases', async () => {
    const two = { ...marenEntry, aliases: ['The Queen', 'Her Grace'] };
    const app = await open({ [ENTRY]: two, [PATCH]: { ...marenEntry, aliases: ['Her Grace'] } });
    const first = await screen.findByTestId(kbTestIds.aliasRemove('The Queen'));
    const second = screen.getByTestId(kbTestIds.aliasRemove('Her Grace'));

    // Both land before the first request does.
    await act(async () => {
      void fireEvent.press(first);
      void fireEvent.press(second);
    });

    await waitFor(() => expect(patchBodies(app)).toEqual([{ aliases: ['Her Grace'] }]));
  });
});

describe('Entry screen: a reader', () => {
  const view = { 'GET /api/books/book-1': bookHeader({ id: 'book-1', role: 'view' }) };

  it('Entry_ForAViewUser_ShouldShowTheEntryWithNoEditControls', async () => {
    await open(view);

    expect(await screen.findByTestId(kbTestIds.entryName)).toHaveTextContent('Maren');
    await waitFor(() => expect(screen.getByTestId(kbTestIds.summary).props.editable).toBe(false));
    expect(screen.getByTestId(kbTestIds.alias('The Queen'))).toBeOnTheScreen();
    expect(screen.queryByTestId(kbTestIds.aliasAdd)).toBeNull();
    expect(screen.queryByTestId(kbTestIds.aliasRemove('The Queen'))).toBeNull();
    expect(screen.getByTestId(`${kbTestIds.always}-switch`).props.disabled).toBe(true);
  });

  it('Entry_ForAViewUser_ShouldNotOfferRecordToEntry', async () => {
    const app = await open(view);
    await screen.findByTestId(kbTestIds.entryName);
    await waitFor(() => expect(screen.getByTestId(kbTestIds.summary).props.editable).toBe(false));

    await userEvent.press(screen.getByTestId(kbTestIds.dockRecord));

    expect(app.getPathname()).toBe('/book/book-1/knowledge/maren');
  });

  it('Entry_ForAWriteUser_ShouldKeepEveryEditControl', async () => {
    await open();

    expect(await screen.findByTestId(kbTestIds.aliasAdd)).toBeOnTheScreen();
    expect(screen.getByTestId(kbTestIds.summary).props.editable).toBe(true);
    expect(screen.getByTestId(`${kbTestIds.always}-switch`).props.disabled).toBe(false);
  });
});
