import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import { recordLink, type RecordTarget } from '~/links';
import { kbEntry, kbEntrySummary } from '~/test-support/fakes';
import { kbEntryKey } from '@codex/core/api/kbEntries';
import { kbKey } from '@codex/core/api/kb';
import { queryClient } from '~/queryClient';
import type { FakeHandler } from '~/test-support/fakeApi';
import { createFakeSpeechEngine } from '~/test-support/fakeSpeechEngine';
import { renderApp } from '~/test-support/renderScreen';
import { audioHarness } from '~/test-support/mocks/mockExpoAudio';
import { deletedFiles, staleFiles } from './test-support/mockExpoFileSystem';
import { recordTestIds } from './testIds';

jest.mock('expo-file-system', () => jest.requireActual('./test-support/mockExpoFileSystem'));

const sceneTarget: RecordTarget = { kind: 'scene', id: 'scene-1' };
const entryTarget: RecordTarget = { kind: 'entry', id: 'entry-1' };

const savedEntry = (overrides: Parameters<typeof kbEntry>[0] = {}) =>
  kbEntry({ bookId: 'book-1', ...overrides });
const writeRoutes: Record<string, FakeHandler> = {
  'POST /api/books/book-1/kb/entries': () => savedEntry({ id: 'entry-new', name: 'Tobin' }),
  'PATCH /api/kb/entries/entry-new': () => savedEntry({ id: 'entry-new' }),
  'PATCH /api/kb/entries/entry-1': () => savedEntry(),
  // A function: a bare `undefined` value reads as "no such route" in the fake API.
  'POST /api/scenes/scene-1/kb': () => undefined,
};

async function openRecord(
  target: RecordTarget,
  {
    engine = {},
    api = {},
  }: {
    engine?: Parameters<typeof createFakeSpeechEngine>[0];
    api?: Record<string, FakeHandler>;
  } = {},
) {
  const speechEngine = createFakeSpeechEngine({
    installed: ['base.en'],
    active: 'base.en',
    transcript: 'She lies.',
    ...engine,
  });
  const app = await renderApp({ speechEngine, api: { ...writeRoutes, ...api } });
  await screen.findByTestId('mobile-library-list');
  await act(async () => router.push(recordLink('book-1', target)));
  await screen.findByTestId(recordTestIds.sheet);
  return { app, speechEngine };
}

const press = (testId: string) => fireEvent.press(screen.getByTestId(testId));

async function recordAndStop() {
  await fireEvent.press(await screen.findByTestId(recordTestIds.record));
  await screen.findByTestId(recordTestIds.stop);
  await press(recordTestIds.stop);
  return screen.findByTestId(recordTestIds.transcript);
}

beforeEach(() => {
  deletedFiles.length = 0;
  staleFiles.length = 0;
});

describe('Record sheet: before recording', () => {
  it('ForAScene_ShouldNameTheSceneAndOfferRecord', async () => {
    await openRecord(sceneTarget);

    await waitFor(() =>
      expect(screen.getByTestId(recordTestIds.target)).toHaveTextContent(
        'Scene 1.1 · An Empty Seat',
      ),
    );
    expect(await screen.findByTestId(recordTestIds.record)).toBeOnTheScreen();
    expect(screen.getByTestId(recordTestIds.timer)).toHaveTextContent('00:00');
  });

  it('ForAnEntry_ShouldNameTheEntry', async () => {
    await openRecord(entryTarget);

    await waitFor(() => expect(screen.getByTestId(recordTestIds.target)).toHaveTextContent('Ana'));
  });

  it('WithoutASpeechModel_ShouldSayAndLinkToTheSpeechModelsScreenInsteadOfRecording', async () => {
    const { app } = await openRecord(sceneTarget, { engine: { installed: [], active: null } });

    expect(await screen.findByTestId(recordTestIds.noModel)).toHaveTextContent(/speech model/i);
    expect(screen.queryByTestId(recordTestIds.record)).toBeNull();

    await fireEvent.press(screen.getByTestId(recordTestIds.noModelAction));

    expect(await screen.findByTestId('mobile-speech-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/speech-models');
  });

  it('WhenTheMicrophoneIsDenied_ShouldSayWhyAndNotRecord', async () => {
    audioHarness.granted = false;
    await openRecord(sceneTarget);

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));

    expect(await screen.findByTestId(recordTestIds.micDenied)).toHaveTextContent(/microphone/i);
    expect(audioHarness.startedSegments).toBe(0);
    expect(screen.getByTestId(recordTestIds.record)).toBeOnTheScreen();
  });

  it('WhenTheMicrophoneCannotStart_ShouldShowTheErrorAndAllowAnotherTry', async () => {
    audioHarness.startFailure = 'Audio session busy.';
    await openRecord(sceneTarget);

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));

    expect(await screen.findByTestId(recordTestIds.recordFailure)).toHaveTextContent(
      'Audio session busy.',
    );
    expect(screen.getByTestId(recordTestIds.record)).toBeOnTheScreen();
  });
});

describe('Record sheet: recording', () => {
  it('RecordPauseResumeStop_ShouldRunTheTimerAndTranscribeEverySegmentInOrder', async () => {
    const { speechEngine } = await openRecord(sceneTarget);

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.pause);
    await act(async () => audioHarness.emitSeconds(74));
    expect(screen.getByTestId(recordTestIds.timer)).toHaveTextContent('01:14');

    await press(recordTestIds.pause);
    expect(await screen.findByTestId(recordTestIds.resume)).toBeOnTheScreen();
    await act(async () => audioHarness.emitSeconds(30));
    expect(screen.getByTestId(recordTestIds.timer)).toHaveTextContent('01:14');

    await press(recordTestIds.resume);
    await screen.findByTestId(recordTestIds.pause);
    await act(async () => audioHarness.emitSeconds(6));
    expect(screen.getByTestId(recordTestIds.timer)).toHaveTextContent('01:20');

    await press(recordTestIds.stop);
    expect(await screen.findByTestId(recordTestIds.transcript)).toHaveDisplayValue(
      'She lies. She lies.',
    );
    expect(speechEngine.transcribed).toEqual([
      'file:///cache/codex-recordings/segment-0.wav',
      'file:///cache/codex-recordings/segment-1.wav',
    ]);
    expect(audioHarness.startedSegments).toBe(2);
  });

  it('Discard_WhileRecording_ShouldDeleteTheAudioCloseAndSaveNothing', async () => {
    const { app, speechEngine } = await openRecord(sceneTarget);
    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await press(recordTestIds.pause);
    await screen.findByTestId(recordTestIds.resume);
    await press(recordTestIds.resume);
    await screen.findByTestId(recordTestIds.discard);

    await press(recordTestIds.discard);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(deletedFiles).toEqual([
      'file:///cache/codex-recordings/segment-0.wav',
      'file:///cache/codex-recordings/segment-1.wav',
    ]);
    expect(audioHarness.recording).toBe(false);
    expect(speechEngine.transcribed).toEqual([]);
    expect(app.api.calls.filter((call) => call.method !== 'GET')).toEqual([]);
  });

  it('ClosingTheSheet_WhileRecording_ShouldStopTheMicrophone', async () => {
    const { app } = await openRecord(sceneTarget);
    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.stop);

    await press(`${recordTestIds.sheet}-close`);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(audioHarness.recording).toBe(false);
    expect(audioHarness.streamStops).toBeGreaterThan(0);
  });
});

describe('Record sheet: transcribing', () => {
  it('WhenTranscriptionFails_ShouldShowTheErrorAndTryAgainOnTheSameAudio', async () => {
    const { speechEngine } = await openRecord(sceneTarget);
    speechEngine.failNextTranscription('The model ran out of memory.');

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.stop);
    await press(recordTestIds.stop);

    expect(await screen.findByTestId(recordTestIds.transcribeError)).toHaveTextContent(
      'The model ran out of memory.',
    );
    expect(deletedFiles).toEqual([]);

    await press(recordTestIds.retry);

    expect(await screen.findByTestId(recordTestIds.transcript)).toHaveDisplayValue('She lies.');
    expect(speechEngine.transcribed).toEqual([
      'file:///cache/codex-recordings/segment-0.wav',
      'file:///cache/codex-recordings/segment-0.wav',
    ]);
  });

  it('WhenTranscriptionFails_Discard_ShouldDeleteTheAudioAndClose', async () => {
    const { app, speechEngine } = await openRecord(sceneTarget);
    speechEngine.failNextTranscription();
    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.stop);
    await press(recordTestIds.stop);
    await screen.findByTestId(recordTestIds.transcribeError);

    await press(recordTestIds.discard);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-0.wav']);
  });
});

describe('Record sheet: landing the text from a scene', () => {
  it('NewEntry_ShouldCreateTheEntryWithTheTranscriptAsItsSummaryLinkItToTheSceneAndClose', async () => {
    const { app } = await openRecord(sceneTarget);
    await recordAndStop();

    expect(screen.getByTestId(recordTestIds.save)).toBeDisabled();
    await fireEvent.changeText(screen.getByTestId(recordTestIds.entryName), 'Tobin');
    await fireEvent.changeText(
      screen.getByTestId(recordTestIds.transcript),
      'She lies, very level.',
    );
    await press(recordTestIds.save);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries').map((c) => c.body)).toEqual([
      { categoryId: 'category-1', name: 'Tobin' },
    ]);
    expect(app.api.callsTo('PATCH /api/kb/entries/entry-new').map((c) => c.body)).toEqual([
      { summary: 'She lies, very level.' },
    ]);
    expect(app.api.callsTo('POST /api/scenes/scene-1/kb').map((c) => c.body)).toEqual([
      { entryId: 'entry-new' },
    ]);
    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-0.wav']);
  });

  it('NewEntry_ShouldCreateItInTheChosenCategory', async () => {
    const { app } = await openRecord(sceneTarget, {
      api: {
        'GET /api/books/book-1/kb': {
          categories: [
            { id: 'category-1', name: 'Characters', fieldDefs: [] },
            { id: 'category-2', name: 'Places', fieldDefs: [] },
          ],
          entries: [],
        },
      },
    });
    await recordAndStop();

    await fireEvent.changeText(screen.getByTestId(recordTestIds.entryName), 'The Abbey');
    await press(recordTestIds.category('category-2'));
    await press(recordTestIds.save);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries')[0]!.body).toEqual({
      categoryId: 'category-2',
      name: 'The Abbey',
    });
  });

  it('NewEntry_WhenALaterRequestFailsAndIsRetried_ShouldNotCreateTheEntryTwice', async () => {
    let attempts = 0;
    const { app } = await openRecord(sceneTarget, {
      api: {
        'POST /api/scenes/scene-1/kb': () =>
          ++attempts === 1
            ? { status: 500, body: { error: 'Boom', code: 'internal_error' } }
            : undefined,
      },
    });
    await recordAndStop();
    await fireEvent.changeText(screen.getByTestId(recordTestIds.entryName), 'Tobin');

    await press(recordTestIds.save);
    expect(await screen.findByTestId(recordTestIds.saveError)).toBeOnTheScreen();
    expect(screen.getByTestId(recordTestIds.transcript)).toBeOnTheScreen();
    await press(recordTestIds.save);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries')).toHaveLength(1);
    expect(app.api.callsTo('PATCH /api/kb/entries/entry-new')).toHaveLength(1);
    expect(app.api.callsTo('POST /api/scenes/scene-1/kb')).toHaveLength(2);
  });

  it('AddToAnEntry_ShouldAppendToTheEntrysSummaryAndLinkItToTheScene', async () => {
    const { app } = await openRecord(sceneTarget);
    await recordAndStop();

    await press(recordTestIds.modeAddToAnEntry);
    expect(screen.getByTestId(recordTestIds.save)).toBeDisabled();
    await press(recordTestIds.pickEntry('entry-1'));
    expect(screen.getByTestId(recordTestIds.pickEntry('entry-1'))).toHaveTextContent(/Selected/);
    await press(recordTestIds.save);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('PATCH /api/kb/entries/entry-1').map((c) => c.body)).toEqual([
      { summary: 'The queen who does not come.\n\nShe lies.' },
    ]);
    expect(app.api.callsTo('POST /api/scenes/scene-1/kb').map((c) => c.body)).toEqual([
      { entryId: 'entry-1' },
    ]);
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries')).toEqual([]);
  });

  it('AddToAnEntry_ShouldFilterTheEntriesBySearch', async () => {
    await openRecord(sceneTarget);
    await recordAndStop();
    await press(recordTestIds.modeAddToAnEntry);
    expect(screen.getByTestId(recordTestIds.pickEntry('entry-1'))).toBeOnTheScreen();

    await fireEvent.changeText(screen.getByPlaceholderText('Search entries'), 'zzz');

    expect(screen.queryByTestId(recordTestIds.pickEntry('entry-1'))).toBeNull();
  });

  it('Discard_FromTheReview_ShouldDeleteTheAudioWithoutSaving', async () => {
    const { app } = await openRecord(sceneTarget);
    await recordAndStop();

    await press(recordTestIds.discard);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-0.wav']);
    expect(app.api.calls.filter((call) => call.method !== 'GET')).toEqual([]);
  });
});

describe('Record sheet: landing the text from an entry', () => {
  it('AddToEntry_ShouldAppendToTheSummaryAndNotLinkAScene', async () => {
    const { app } = await openRecord(entryTarget);
    await recordAndStop();

    expect(screen.queryByTestId(recordTestIds.modeNewEntry)).toBeNull();
    await userEvent.press(screen.getByTestId(recordTestIds.save));

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('PATCH /api/kb/entries/entry-1').map((c) => c.body)).toEqual([
      { summary: 'The queen who does not come.\n\nShe lies.' },
    ]);
    expect(app.api.calls.filter((c) => c.path.endsWith('/kb') && c.method === 'POST')).toEqual([]);
  });

  it('KeepAsNote_ShouldBeShownButNotChoosable', async () => {
    await openRecord(entryTarget);
    await recordAndStop();

    await userEvent.press(screen.getByTestId(recordTestIds.modeKeepAsNote));

    expect(screen.getByTestId(recordTestIds.modeKeepAsNote)).toHaveTextContent(/Keep as note/);
    expect(screen.getByTestId(recordTestIds.save)).toBeEnabled();
    expect(screen.getByTestId(recordTestIds.modeAddToEntry).props.accessibilityState).toMatchObject(
      {
        selected: true,
      },
    );
  });
});

describe('Record sheet: Android file URIs and microphone failures', () => {
  it('WhenTheRecorderReportsASingleSlashUri_ShouldDeleteItThroughTheCanonicalUri', async () => {
    audioHarness.oneSlashUris = true;
    const { app, speechEngine } = await openRecord(sceneTarget);
    await recordAndStop();

    await press(recordTestIds.discard);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(speechEngine.transcribed).toEqual(['file:/cache/codex-recordings/segment-0.wav']);
    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-0.wav']);
  });

  it('WhenTheFileCannotBeStarted_ShouldStopTheMicrophoneAndShowTheError', async () => {
    audioHarness.fileStartFailure = 'No space left.';
    await openRecord(sceneTarget);

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));

    expect(await screen.findByTestId(recordTestIds.recordFailure)).toHaveTextContent(
      'No space left.',
    );
    expect(audioHarness.streamStops).toBeGreaterThan(0);
  });

  it('WhenTheFileCannotBeFinalised_ShouldStillDeleteTheFileItWasWriting', async () => {
    const { app } = await openRecord(sceneTarget);
    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.stop);
    audioHarness.fileStopFailure = 'Disk error.';

    await press(recordTestIds.stop);
    expect(await screen.findByTestId(recordTestIds.recordFailure)).toHaveTextContent('Disk error.');
    await press(`${recordTestIds.sheet}-close`);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-0.wav']);
  });

  it('Record_ShouldFirstClearRecordingsAnEarlierSessionLeftBehind', async () => {
    staleFiles.push('file:///cache/codex-recordings/segment-7.wav');
    await openRecord(sceneTarget);

    await fireEvent.press(await screen.findByTestId(recordTestIds.record));
    await screen.findByTestId(recordTestIds.stop);

    expect(deletedFiles).toEqual(['file:///cache/codex-recordings/segment-7.wav']);
  });
});

describe('Record sheet: the Knowledge base on the review step', () => {
  it('WhenTheKnowledgeBaseFailsToLoad_ShouldSayAndRetryInsteadOfShowingNoCategories', async () => {
    let failing = true;
    const { app } = await openRecord(sceneTarget, {
      api: {
        'GET /api/books/book-1/kb': () =>
          failing
            ? { status: 500, body: { error: 'KB down.', code: 'internal_error' } }
            : kbWithTwoEntries(),
      },
    });
    await recordAndStop();

    expect(await screen.findByTestId(recordTestIds.kbError)).toBeOnTheScreen();
    expect(screen.queryByText(/no categories/i)).toBeNull();
    expect(screen.getByTestId(recordTestIds.save)).toBeDisabled();

    failing = false;
    await press(`${recordTestIds.kbError}-retry`);

    expect(await screen.findByTestId(recordTestIds.category('category-1'))).toBeOnTheScreen();
    expect(app.api.callsTo('GET /api/books/book-1/kb').length).toBeGreaterThan(1);
  });
});

const kbWithTwoEntries = () => ({
  categories: [{ id: 'category-1', name: 'Characters', fieldDefs: [] }],
  entries: [
    kbEntrySummary({ id: 'entry-1', name: 'Ana' }),
    kbEntrySummary({ id: 'entry-2', name: 'Tobin' }),
  ],
});

describe('Record sheet: a retry after a partial save', () => {
  it('WhenAnotherEntryIsPickedAfterAFailedLink_ShouldWriteAndLinkTheNewEntryNotTheOldOne', async () => {
    let linkAttempts = 0;
    const { app } = await openRecord(sceneTarget, {
      api: {
        'GET /api/books/book-1/kb': kbWithTwoEntries(),
        'PATCH /api/kb/entries/entry-2': () => savedEntry({ id: 'entry-2' }),
        'GET /api/kb/entries/entry-2': () => savedEntry({ id: 'entry-2', summary: '' }),
        'POST /api/scenes/scene-1/kb': () =>
          ++linkAttempts === 1
            ? { status: 500, body: { error: 'Boom', code: 'internal_error' } }
            : undefined,
      },
    });
    await recordAndStop();
    await press(recordTestIds.modeAddToAnEntry);
    await press(recordTestIds.pickEntry('entry-1'));
    await press(recordTestIds.save);
    expect(await screen.findByTestId(recordTestIds.saveError)).toBeOnTheScreen();

    await press(recordTestIds.pickEntry('entry-2'));
    await press(recordTestIds.save);

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(app.api.callsTo('PATCH /api/kb/entries/entry-2').map((c) => c.body)).toEqual([
      { summary: 'She lies.' },
    ]);
    expect(app.api.callsTo('POST /api/scenes/scene-1/kb').map((c) => c.body)).toEqual([
      { entryId: 'entry-1' },
      { entryId: 'entry-2' },
    ]);
  });
});

describe('Record sheet: caches after landing', () => {
  const kbReads = (app: Awaited<ReturnType<typeof openRecord>>['app']) =>
    app.api.callsTo('GET /api/books/book-1/kb').length;
  const outlineReads = (app: Awaited<ReturnType<typeof openRecord>>['app']) =>
    app.api.callsTo('GET /api/books/book-1/outline').length;

  it('NewEntry_ShouldRefetchTheKnowledgeBaseAndTheOutline', async () => {
    const { app } = await openRecord(sceneTarget);
    await recordAndStop();
    const [kbBefore, outlineBefore] = [kbReads(app), outlineReads(app)];
    queryClient.setQueryData(kbEntryKey('entry-new'), savedEntry({ id: 'entry-new' }));
    await fireEvent.changeText(screen.getByTestId(recordTestIds.entryName), 'Tobin');

    await press(recordTestIds.save);

    await waitFor(() => expect(kbReads(app)).toBe(kbBefore + 1));
    await waitFor(() => expect(outlineReads(app)).toBe(outlineBefore + 1));
    expect(queryClient.getQueryState(kbEntryKey('entry-new'))?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(kbKey('book-1'))?.isInvalidated).toBe(false);
  });

  it('AddToAnEntry_ShouldRefetchTheKnowledgeBaseAndInvalidateTheEntry', async () => {
    const { app } = await openRecord(sceneTarget);
    await recordAndStop();
    const kbBefore = kbReads(app);
    queryClient.setQueryData(kbEntryKey('entry-1'), savedEntry());
    await press(recordTestIds.modeAddToAnEntry);
    await press(recordTestIds.pickEntry('entry-1'));

    await press(recordTestIds.save);

    await waitFor(() => expect(kbReads(app)).toBe(kbBefore + 1));
    expect(queryClient.getQueryState(kbEntryKey('entry-1'))?.isInvalidated).toBe(true);
  });

  it('AddToEntry_FromAnEntry_ShouldRefetchTheKnowledgeBaseAndInvalidateTheEntry', async () => {
    const { app } = await openRecord(entryTarget);
    await recordAndStop();
    const kbBefore = kbReads(app);
    queryClient.setQueryData(kbEntryKey('entry-1'), savedEntry());

    await press(recordTestIds.save);

    await waitFor(() => expect(kbReads(app)).toBe(kbBefore + 1));
    expect(queryClient.getQueryState(kbEntryKey('entry-1'))?.isInvalidated).toBe(true);
  });
});
