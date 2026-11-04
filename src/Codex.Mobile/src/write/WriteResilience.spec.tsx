import {
  keepProseDraft,
  readProseDraft,
  setSceneProse,
  storeUnsavedProse,
} from '@codex/core/api/write';
import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import { AppState, type AppStateStatus } from 'react-native';
import useColorScheme from 'react-native/Libraries/Utilities/useColorScheme';
import { libraryLink, writeLink } from '~/links';
import { mobilePlatform } from '~/mobilePlatform';
import { queryClient } from '~/queryClient';
import { FAKE_BOOK_ID } from '~/test-support/fakes';
import { webViewHarness } from '~/test-support/mocks/reactNativeWebview';
import { syntheticEvent } from './test-support/events';
import { openWrite, pageOf } from './test-support/openWrite';
import { writeOutline } from './test-support/world';
import { writeTestIds } from './testIds';
import { messageEvent } from './test-support/events';
import { encodePageMessage } from '@codex/core/editing/editorBridge';

const down = { status: 500, body: { error: 'Down.', code: 'internal_error' } };

async function readyChapterOne(options: Parameters<typeof openWrite>[0] = {}) {
  const opened = await openWrite(options);
  await opened.page.ready();
  await opened.page.waitForLoads(1);
  return opened;
}

describe('Write: text that cannot be saved is never dropped', () => {
  it('ChapterChange_AfterAFailedSave_ShouldKeepTheEditTryAgainAndGiveItBackOnReturn', async () => {
    const { app, page } = await readyChapterOne({ api: { 'PUT /api/scenes/s1/prose': down } });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Typed offline.\n' });

    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);
    app.api.respond('PUT /api/scenes/s1/prose', (request: { body: unknown }) => ({
      version: (request.body as { version: number }).version + 1,
    }));
    await fireEvent.press(screen.getByTestId(writeTestIds.pillPrevious));
    await page.waitForLoads(3);

    // Coming back tries the save again (the server is up now), so the text returns as saved.
    expect(page.loads()[2]?.scenes[0]).toMatchObject({
      id: 's1',
      prose: 'Typed offline.\n',
      version: 2,
    });
    expect(app.api.callsTo('PUT /api/scenes/s1/prose').at(-1)?.body).toEqual({
      body: 'Typed offline.\n',
      version: 1,
    });
  });

  it('ChapterChange_WhileTheServerStaysDown_ShouldGiveTheUnsavedTextBackOnReturn', async () => {
    const { page } = await readyChapterOne({ api: { 'PUT /api/scenes/s1/prose': down } });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Still typed.\n' });

    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);
    await fireEvent.press(screen.getByTestId(writeTestIds.pillPrevious));
    await page.waitForLoads(3);

    expect(page.loads()[2]?.scenes[0]).toMatchObject({
      id: 's1',
      prose: 'Still typed.\n',
      version: 1,
    });
  });

  it('ChapterChange_AfterAFailedSave_ShouldStillLoadTheNextChapter', async () => {
    const { page } = await readyChapterOne({ api: { 'PUT /api/scenes/s1/prose': down } });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Typed.\n' });

    await fireEvent.press(screen.getByTestId(writeTestIds.pillNext));
    await page.waitForLoads(2);

    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s3']);
  });

  it('Unmount_WithAnUnsavedEdit_ShouldKeepTheDraft', async () => {
    const { page } = await readyChapterOne({ api: { 'PUT /api/scenes/s1/prose': down } });
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Left on the train.\n' });

    // Leaving the book closes Write.
    await act(async () => router.dismissTo(libraryLink()));

    await waitFor(() =>
      expect(readProseDraft(queryClient, FAKE_BOOK_ID, 's1', 1)).toEqual({
        body: 'Left on the train.\n',
        version: 1,
      }),
    );
  });

  it('Reopen_AfterLeavingWithAnOfflineEdit_ShouldAdoptTheWaitingSaveShowItsTextAndSendItOnce', async () => {
    const { app, page } = await readyChapterOne();
    let backOnline: () => void = () => undefined;
    jest.spyOn(mobilePlatform, 'isOffline').mockReturnValue(true);
    jest.spyOn(mobilePlatform, 'onOnline').mockImplementation((listener) => {
      backOnline = listener;
      return () => undefined;
    });
    const original = globalThis.fetch;
    globalThis.fetch = jest.fn((input: Parameters<typeof fetch>[0], init?: RequestInit) =>
      init?.method === 'PUT'
        ? Promise.reject(new TypeError('Network request failed'))
        : original(input, init),
    ) as unknown as typeof fetch;
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Typed on the plane.\n' });

    await act(async () => router.dismissTo(libraryLink()));
    await waitFor(() => expect(saveStatusStore.getState()).toBe('offline'));
    globalThis.fetch = original;
    await act(async () => router.push(writeLink('book-1', {})));
    const again = pageOf();
    await screen.findByTestId(writeTestIds.webView);
    await again.ready();
    await again.waitForLoads(1);

    // The page gets the pending text, and the dot still says the edit is waiting.
    expect(again.loads()[0]?.scenes[0]).toMatchObject({ id: 's1', prose: 'Typed on the plane.\n' });
    expect(saveStatusStore.getState()).toBe('offline');

    app.api.respond('PUT /api/scenes/s1/prose', (request: { body: unknown }) => ({
      version: (request.body as { version: number }).version + 1,
    }));
    jest.mocked(mobilePlatform.isOffline).mockReturnValue(false);
    await act(async () => {
      backOnline();
    });

    await waitFor(() => expect(saveStatusStore.getState()).toBe('saved'));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1);
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')[0]?.body).toEqual({
      body: 'Typed on the plane.\n',
      version: 1,
    });
    await act(async () => jest.advanceTimersByTime(120_000));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1);
  });

  it('Open_WithAKeptDraftForTheServersVersion_ShouldShowTheDraftAndSaveItWhenTheEditorBlurs', async () => {
    const draft = { body: 'Left on the train.\n', version: 1 };
    keepProseDraft(queryClient, FAKE_BOOK_ID, 's1', draft);
    storeUnsavedProse('s1', draft);

    const { app, page } = await readyChapterOne();

    expect(page.loads()[0]?.scenes[0]).toMatchObject({ prose: 'Left on the train.\n', version: 1 });
    await page.say({ type: 'blur', sceneId: 's1' });
    await waitFor(() =>
      expect(app.api.callsTo('PUT /api/scenes/s1/prose')[0]?.body).toEqual({
        body: 'Left on the train.\n',
        version: 1,
      }),
    );
    await waitFor(() => expect(readProseDraft(queryClient, FAKE_BOOK_ID, 's1', 1)).toBeUndefined());
  });

  it('Open_WithADraftForAnOlderVersion_ShouldShowTheServersCopy', async () => {
    const draft = { body: 'Stale draft.\n', version: 0 };
    keepProseDraft(queryClient, FAKE_BOOK_ID, 's1', draft);
    storeUnsavedProse('s1', draft);

    const { page } = await readyChapterOne();

    expect(page.loads()[0]?.scenes[0]).toMatchObject({ prose: 'First.\n', version: 1 });
  });

  it('Background_ShouldSaveEverySceneNow', async () => {
    let onAppState: (state: AppStateStatus) => void = () => {};
    jest.spyOn(AppState, 'addEventListener').mockImplementation((_type, listener) => {
      onAppState = listener;
      return { remove: () => {} };
    });
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Just in time.\n' });

    await act(async () => onAppState('background'));

    await waitFor(() => expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1));
  });

  it('ReadOnly_ShouldIgnoreChangesFromThePage', async () => {
    const { app, page } = await readyChapterOne({
      api: {
        [`GET /api/books/${FAKE_BOOK_ID}`]: {
          id: FAKE_BOOK_ID,
          title: 'The Empty Seat',
          subtitle: null,
          authorName: 'JD',
          seriesId: null,
          seriesName: null,
          seriesNumber: null,
          status: 'writing',
          coverPalette: 'cover-3',
          coverInitials: 'ES',
          wordCount: 0,
          targetWords: 0,
          scope: [],
          referenceBadgeCount: null,
          referenceBadgeTone: null,
          lastEditedAt: '2026-10-20T09:00:00Z',
          lastEditedLabel: '2h ago',
          coverUrl: null,
          role: 'comment',
          owner: { id: 'u', displayName: 'JD' },
        },
      },
    });
    expect(page.loads()[0]?.readOnly).toBe(true);

    await page.say({ type: 'change', sceneId: 's1', markdown: 'Sneaky.\n' });
    await page.say({ type: 'blur', sceneId: 's1' });

    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(0);
  });
});

describe('Write: block operations', () => {
  it('SplitScene_ShouldSaveTheChaptersOtherScenesFirst', async () => {
    const { app, page } = await readyChapterOne({
      api: { 'POST /api/scenes/s1/split': { newSceneId: 's9', version: 2, newSceneVersion: 1 } },
    });
    await page.say({ type: 'change', sceneId: 's2', markdown: 'Second, typed.\n' });

    await page.say({
      type: 'action',
      sceneId: 's1',
      action: 'newScene',
      headBody: 'Head.\n',
      tailBody: 'Tail.\n',
    });

    await waitFor(() => expect(app.api.callsTo('POST /api/scenes/s1/split')).toHaveLength(1));
    const order = app.api.calls.map((call) => `${call.method} ${call.path}`);
    expect(order.indexOf('PUT /api/scenes/s2/prose')).toBeGreaterThan(-1);
    expect(order.indexOf('PUT /api/scenes/s2/prose')).toBeLessThan(
      order.indexOf('POST /api/scenes/s1/split'),
    );
  });

  it('SplitScene_WhenAnotherSceneCannotBeSaved_ShouldNotSplitAndSayWhy', async () => {
    const { app, page } = await readyChapterOne({ api: { 'PUT /api/scenes/s2/prose': down } });
    await page.say({ type: 'change', sceneId: 's2', markdown: 'Unsaveable.\n' });

    await page.say({
      type: 'action',
      sceneId: 's1',
      action: 'newScene',
      headBody: 'Head.\n',
      tailBody: 'Tail.\n',
    });

    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent(
      /Your changes could not be saved yet/,
    );
    expect(app.api.callsTo('POST /api/scenes/s1/split')).toHaveLength(0);
  });

  it('Change_WhileABlockOperationRuns_ShouldBeHeldAndNeverSaved', async () => {
    const { app, page } = await readyChapterOne({
      api: { 'POST /api/scenes/s1/beats': { beatId: 'b', version: 2 } },
    });
    const send = (message: Parameters<typeof encodePageMessage>[0]) =>
      webViewHarness.last().props.onMessage?.(messageEvent(encodePageMessage(message)));

    await act(async () => {
      send({
        type: 'action',
        sceneId: 's1',
        action: 'beat',
        beatId: 'b',
        body: 'First.\n\n::beat{id="b"}\n',
      });
      send({ type: 'change', sceneId: 's1', markdown: 'First.\n' });
    });
    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    await page.say({ type: 'blur', sceneId: 's1' });

    expect(app.api.callsTo('POST /api/scenes/s1/beats')).toHaveLength(1);
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(0);
  });

  it('Action_WhileAnotherIsRunning_ShouldBeRefused', async () => {
    const { app, page } = await readyChapterOne({
      api: { 'POST /api/scenes/s1/beats': { beatId: 'b', version: 2 } },
    });
    const send = (message: Parameters<typeof encodePageMessage>[0]) =>
      webViewHarness.last().props.onMessage?.(messageEvent(encodePageMessage(message)));
    const beatAction = (beatId: string) =>
      ({
        type: 'action',
        sceneId: 's1',
        action: 'beat',
        beatId,
        body: `x\n\n::beat{id="${beatId}"}\n`,
      }) as const;

    await act(async () => {
      send(beatAction('b'));
      send(beatAction('c'));
    });

    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent(
      'Finish the current change first.',
    );
    await waitFor(() => expect(page.sent().some((m) => m.type === 'setScene')).toBe(true));
    expect(app.api.callsTo('POST /api/scenes/s1/beats')).toHaveLength(1);
  });
});

describe('Write: card details from the scene sheet', () => {
  const editSummary = (summary: string) =>
    act(async () => {
      const outline = writeOutline();
      outline.acts[0]!.chapters[0]!.scenes[0]!.summary = summary;
      queryClient.setQueryData(['outline', FAKE_BOOK_ID], outline);
    });

  it('ChangedSummaryOrLabels_ShouldSendSceneMetaWithoutTouchingProse', async () => {
    const { page } = await readyChapterOne();

    await act(async () => {
      const outline = writeOutline();
      const scene = outline.acts[0]!.chapters[0]!.scenes[0]!;
      scene.summary = 'A new summary.';
      scene.labels = [{ id: 'l1', name: 'Revising', color: null }];
      queryClient.setQueryData(['outline', FAKE_BOOK_ID], outline);
    });

    await waitFor(() =>
      expect(page.sent().filter((m) => m.type === 'sceneMeta')).toEqual([
        {
          type: 'sceneMeta',
          sceneId: 's1',
          title: 'The doors',
          labels: ['Revising'],
          words: 3000,
          summary: 'A new summary.',
          chips: [],
        },
      ]),
    );
    expect(page.loads()).toHaveLength(1);
    expect(page.sent().some((m) => m.type === 'setScene')).toBe(false);
  });

  it('SameDetailsAgain_ShouldNotResend', async () => {
    const { page } = await readyChapterOne();
    await editSummary('Once.');
    await waitFor(() => expect(page.sent().filter((m) => m.type === 'sceneMeta')).toHaveLength(1));

    await editSummary('Once.');

    expect(page.sent().filter((m) => m.type === 'sceneMeta')).toHaveLength(1);
  });

  it('ANewScene_ShouldSaveThePendingTextThenLoadTheChapterAgain', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's2', markdown: 'Typed.\n' });

    await act(async () => {
      const outline = writeOutline();
      const chapter = outline.acts[0]!.chapters[0]!;
      chapter.scenes = [...chapter.scenes, { ...chapter.scenes[0]!, id: 's9', number: '1.3' }];
      queryClient.setQueryData(['outline', FAKE_BOOK_ID], outline);
    });

    await page.waitForLoads(2);
    expect(page.loads()[1]?.scenes.map((s) => s.id)).toEqual(['s1', 's2', 's9']);
    expect(page.loads()[1]?.scenes[1]?.prose).toBe('Typed.\n');
    expect(app.api.callsTo('PUT /api/scenes/s2/prose')).toHaveLength(1);
  });

  it('OwnSaves_ShouldNotLookLikeCardChanges', async () => {
    const { page } = await readyChapterOne();

    await act(async () => {
      setSceneProse(queryClient, FAKE_BOOK_ID, { sceneId: 's1', body: 'Else.\n', version: 5 });
    });

    expect(page.sent().filter((m) => m.type === 'sceneMeta')).toEqual([]);
  });
});

describe('Write: the page', () => {
  it('ProcessGone_ShouldSaveThenRemountThePageAndLoadAgain', async () => {
    const { app, page } = await readyChapterOne();
    await page.say({ type: 'change', sceneId: 's1', markdown: 'Before the crash.\n' });
    const crashed = webViewHarness.last();

    await act(async () => {
      crashed.props.onRenderProcessGone?.(syntheticEvent({ didCrash: true }));
    });

    await waitFor(() => expect(webViewHarness.last()).not.toBe(crashed));
    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(1);
    await page.ready();
    await waitFor(() => expect(webViewHarness.last().sentToPage()).toHaveLength(1));
    expect(webViewHarness.last().sentToPage()[0]).toMatchObject({
      type: 'load',
      scenes: [{ id: 's1', prose: 'Before the crash.\n' }, { id: 's2' }],
    });
  });

  it('IosContentProcessTerminated_ShouldDoTheSame', async () => {
    const { page } = await readyChapterOne();
    const crashed = webViewHarness.last();

    await act(async () => {
      crashed.props.onContentProcessDidTerminate?.(
        syntheticEvent({
          url: 'x',
          loading: false,
          title: '',
          canGoBack: false,
          canGoForward: false,
          lockIdentifier: 0,
        }),
      );
    });

    await waitFor(() => expect(webViewHarness.last()).not.toBe(crashed));
    await page.ready();
    await waitFor(() => expect(webViewHarness.last().sentToPage()).toHaveLength(1));
  });

  it('ThemeChange_WhileEditing_ShouldWaitForTheEditorToLoseFocus', async () => {
    const { page } = await readyChapterOne();
    await page.say({ type: 'focus', sceneId: 's1' });

    jest.mocked(useColorScheme).mockReturnValue('dark');
    // Any state change re-renders the screen, which reads the device scheme.
    await page.say({
      type: 'state',
      bold: false,
      italic: false,
      quote: false,
      canUndo: true,
      canRedo: false,
    });
    expect(page.loads()).toHaveLength(1);

    await page.say({ type: 'blur', sceneId: 's1' });

    await page.waitForLoads(2);
    expect(page.loads()[1]?.theme).toBe('dark');
  });

  it('OpenInWrite_ForTheSceneAlreadyOpen_ShouldScrollToItAgain', async () => {
    const { page } = await readyChapterOne();
    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's2' })));
    await waitFor(() => expect(page.sent().filter((m) => m.type === 'scrollTo')).toHaveLength(1));

    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's2' })));

    await waitFor(() => expect(page.sent().filter((m) => m.type === 'scrollTo')).toHaveLength(2));
    expect(page.loads()).toHaveLength(1);
  });
});
