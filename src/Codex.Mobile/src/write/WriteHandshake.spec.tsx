import { EDITOR_BRIDGE_VERSION } from '@codex/core/editing/editorBridge';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { webViewHarness } from '~/test-support/mocks/reactNativeWebview';
import { bookHeader, FAKE_BOOK_ID } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { EDITOR_MOUNT_TIMEOUT_MS, EDITOR_READY_TIMEOUT_MS } from './useEditorSession';
import { errorEvent, httpErrorEvent } from './test-support/events';
import { writeTestIds } from './testIds';
import { openWrite, pageOf } from './test-support/openWrite';
import { beat, writeRoutes } from './test-support/world';

describe('Write: the handshake', () => {
  it('Write_ShouldLoadThePageFromTheServerAndWaitForReadyBeforeSendingAnything', async () => {
    const { page } = await openWrite();

    expect(webViewHarness.last().props.source).toEqual({
      uri: 'https://codex.test/mobile-editor.html',
    });
    expect(page.sent()).toEqual([]);
  });

  it('Ready_ShouldAnswerWithALoadOfTheFirstChaptersScenes', async () => {
    const { page } = await openWrite({
      api: {
        [`GET /api/books/${FAKE_BOOK_ID}/beats`]: { bookId: FAKE_BOOK_ID, beats: [beat()] },
      },
    });

    await page.ready();
    await page.waitForLoads(1);

    const load = page.loads()[0]!;
    expect(load).toMatchObject({
      v: EDITOR_BRIDGE_VERSION,
      theme: 'light',
      density: 'touch',
      readOnly: false,
      scrollToSceneId: null,
    });
    expect(load.scenes.map((s) => [s.id, s.number, s.prose, s.version])).toEqual([
      ['s1', '1.1', 'First.\n', 1],
      ['s2', '1.2', 'Second.\n', 2],
    ]);
    expect(load.scenes[0]?.beats).toEqual([
      {
        id: 'beat-1',
        title: '',
        body: 'Tobin counts the guards.',
        targetWordCount: 400,
        hidden: false,
      },
    ]);
    expect(load.entries).toEqual([
      { id: 'entry-1', name: 'Ana', category: 'Characters', aliases: [] },
    ]);
    expect(screen.getByTestId(writeTestIds.pillLabel)).toHaveTextContent('1 · An Empty Seat');
  });

  it('Ready_InTheDarkTheme_ShouldSendTheDarkTheme', async () => {
    const app = await renderApp({
      url: '/book/book-1/write',
      api: writeRoutes(),
      scheme: 'dark',
    });
    await screen.findByTestId(writeTestIds.webView);

    await pageOf().ready();

    await waitFor(() => expect(webViewHarness.last().sentToPage()).toHaveLength(1));
    expect(webViewHarness.last().sentToPage()[0]).toMatchObject({ type: 'load', theme: 'dark' });
    expect(app.getPathname()).toBe('/book/book-1/write');
  });

  it('Ready_WithAnotherBridgeVersion_ShouldShowTheErrorAndNeverLoad', async () => {
    const { page } = await openWrite();

    await page.say({ type: 'ready', v: EDITOR_BRIDGE_VERSION + 1 });

    expect(await screen.findByTestId(writeTestIds.pageError)).toHaveTextContent(
      new RegExp(
        `version ${EDITOR_BRIDGE_VERSION + 1} but this app speaks version ${EDITOR_BRIDGE_VERSION}`,
      ),
    );
    expect(screen.queryByTestId(writeTestIds.webView)).toBeNull();
  });

  it('Ready_WhenTheUserCanOnlyView_ShouldLoadReadOnlyAndNeverOfferTheAccessoryBar', async () => {
    const { page } = await openWrite({
      api: { [`GET /api/books/${FAKE_BOOK_ID}`]: bookHeader({ id: FAKE_BOOK_ID, role: 'view' }) },
    });

    await page.ready();
    await page.waitForLoads(1);
    await page.say({ type: 'focus', sceneId: 's1' });

    expect(page.loads()[0]?.readOnly).toBe(true);
    expect(screen.queryByTestId(writeTestIds.accessoryBar)).toBeNull();
    expect(screen.getByTestId(writeTestIds.pillNext)).toBeOnTheScreen();
  });

  it('Ready_WhenThePageReloads_ShouldLoadAgain', async () => {
    const { page } = await openWrite();
    await page.ready();
    await page.waitForLoads(1);

    await page.ready();

    await page.waitForLoads(2);
  });
});

describe('Write: states', () => {
  it('Write_WhileTheBookLoads_ShouldShowLoading', async () => {
    await renderApp({
      url: '/book/book-1/write',
      api: writeRoutes(),
    });
    expect(await screen.findByTestId(writeTestIds.loading)).toBeOnTheScreen();
    expect(screen.queryByTestId(writeTestIds.webView)).toBeNull();
  });

  it('Write_WhenTheManuscriptFails_ShouldShowTheErrorAndRetryLoadsIt', async () => {
    const { api } = await renderApp({
      url: '/book/book-1/write',
      api: {
        ...writeRoutes(),
        [`GET /api/books/${FAKE_BOOK_ID}/manuscript`]: {
          status: 500,
          body: { error: 'The manuscript is unavailable.', code: 'internal_error' },
        },
      },
    });
    expect(await screen.findByTestId(writeTestIds.error)).toHaveTextContent(
      'The manuscript is unavailable.',
    );

    api.respond(
      `GET /api/books/${FAKE_BOOK_ID}/manuscript`,
      writeRoutes()[`GET /api/books/${FAKE_BOOK_ID}/manuscript`],
    );
    await fireEvent.press(screen.getByTestId(`${writeTestIds.error}-retry`));

    expect(await screen.findByTestId(writeTestIds.webView)).toBeOnTheScreen();
  });

  it('Write_WhenTheBookHasNoChapters_ShouldSayThereAreNone', async () => {
    await renderApp({
      url: '/book/book-1/write',
      api: {
        ...writeRoutes(),
        [`GET /api/books/${FAKE_BOOK_ID}/outline`]: { bookId: FAKE_BOOK_ID, acts: [] },
      },
    });

    expect(await screen.findByTestId(writeTestIds.empty)).toHaveTextContent(
      'This book has no chapters yet.',
    );
    expect(screen.queryByTestId(writeTestIds.webView)).toBeNull();
  });

  it('Page_WhenTheWebViewHitsAnHttpError_ShouldShowTheErrorAndRetryRemountsThePage', async () => {
    const { page } = await openWrite();
    const first = webViewHarness.last();

    await waitFor(() => first.props.onHttpError?.(httpErrorEvent(404)));
    expect(await screen.findByTestId(writeTestIds.pageError)).toHaveTextContent(
      'The editor could not be loaded. (HTTP 404).',
    );

    await fireEvent.press(screen.getByTestId(`${writeTestIds.pageError}-retry`));
    await screen.findByTestId(writeTestIds.webView);
    await page.ready();

    await waitFor(() => expect(webViewHarness.last().sentToPage()).toHaveLength(1));
    expect(webViewHarness.last()).not.toBe(first);
  });

  it('Page_WhenItLoadsButNeverSaysReady_ShouldFailTenSecondsAfterTheLoadAndRetryRemountsThePage', async () => {
    const { page } = await openWrite();
    const first = webViewHarness.last();
    await act(async () => first.props.onLoadEnd?.({} as never));
    expect(screen.queryByTestId(writeTestIds.pageError)).toBeNull();

    await act(async () => jest.advanceTimersByTime(EDITOR_READY_TIMEOUT_MS));

    expect(await screen.findByTestId(writeTestIds.pageError)).toHaveTextContent(
      /^The editor did not start\./,
    );
    await fireEvent.press(screen.getByTestId(`${writeTestIds.pageError}-retry`));
    await screen.findByTestId(writeTestIds.webView);
    expect(webViewHarness.last()).not.toBe(first);
    await page.ready();
    await waitFor(() => expect(webViewHarness.last().sentToPage()).toHaveLength(1));
  });

  it('Page_WhenItIsStillLoadingAfterTenSeconds_ShouldWaitAndFailAtTheThirtySecondCap', async () => {
    await openWrite();
    await act(async () => jest.advanceTimersByTime(EDITOR_READY_TIMEOUT_MS * 2));
    expect(screen.queryByTestId(writeTestIds.pageError)).toBeNull();

    await act(async () => jest.advanceTimersByTime(EDITOR_MOUNT_TIMEOUT_MS));

    expect(await screen.findByTestId(writeTestIds.pageError)).toHaveTextContent(
      /^The editor did not start\./,
    );
  });

  it('Page_WhenItSaysReadyInTime_ShouldNotFailLater', async () => {
    const { page } = await openWrite();
    await act(async () => webViewHarness.last().props.onLoadEnd?.({} as never));
    await page.ready();

    await act(async () => jest.advanceTimersByTime(EDITOR_MOUNT_TIMEOUT_MS * 2));

    expect(screen.queryByTestId(writeTestIds.pageError)).toBeNull();
  });

  it('Page_WhenTheWebViewCannotConnect_ShouldShowItsDescription', async () => {
    await openWrite();

    await waitFor(() =>
      webViewHarness.last().props.onError?.(errorEvent('net::ERR_CONNECTION_REFUSED')),
    );

    expect(await screen.findByTestId(writeTestIds.pageError)).toHaveTextContent(
      'net::ERR_CONNECTION_REFUSED',
    );
  });

  it('Page_WhenItReportsAnErrorAfterLoading_ShouldShowANoticeAndKeepTheEditor', async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => {});
    const { page } = await openWrite();
    await page.ready();
    await page.waitForLoads(1);

    await page.say({ type: 'error', message: 'Could not draw a beat.' });

    expect(await screen.findByTestId(writeTestIds.notice)).toHaveTextContent(
      'Could not draw a beat.',
    );
    expect(screen.getByTestId(writeTestIds.webView)).toBeOnTheScreen();
  });

  it('Page_WhenItSendsGarbage_ShouldIgnoreItAndKeepWorking', async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => {});
    const { page } = await openWrite();
    await page.ready();
    await page.waitForLoads(1);

    await page.sayRaw('not json');

    expect(screen.getByTestId(writeTestIds.webView)).toBeOnTheScreen();
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('ignored a message'));
  });
});

describe('Write: beats', () => {
  it('Load_ShouldCarryAHiddenBeatAsHidden', async () => {
    const { page } = await openWrite({
      api: {
        [`GET /api/books/${FAKE_BOOK_ID}/beats`]: {
          bookId: FAKE_BOOK_ID,
          beats: [beat({ isHidden: true })],
        },
      },
    });

    await page.ready();
    await page.waitForLoads(1);

    expect(page.loads()[0]?.scenes[0]?.beats[0]).toMatchObject({ id: 'beat-1', hidden: true });
  });
});
