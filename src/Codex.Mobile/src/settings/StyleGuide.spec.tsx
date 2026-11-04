import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { saveStatusStore } from '@codex/core/editing/saveStatusStore';
import { mobilePlatform } from '~/mobilePlatform';
import { bookHeader } from '~/test-support/fakes';
import { bookSettings } from '~/test-support/fakes/settings';
import type { FakeRequest } from '~/test-support/fakeApi';
import { renderApp } from '~/test-support/renderScreen';
import { shellTestIds } from '~/ui';
import { layoutSettings, publicationSettings } from './test-support/fakes';
import { settingsTestIds as ids } from './testIds';

const BOOK = '/api/books/book-1';
const styleGuideUrl = '/book/book-1/settings/style-guide';

beforeEach(() => saveStatusStore.set('saved'));

/**
 * Takes over PATCH requests on the installed fake network: held until `answer` (so a save can be in flight), or
 * refused as a network failure with `reject`. Everything else goes to the fake API; `restore` hands PATCH back.
 */
function holdPatches({ reject = false }: { reject?: boolean } = {}) {
  const original = globalThis.fetch;
  const waiting: ((response: Response) => void)[] = [];
  const state = { requests: 0 };
  globalThis.fetch = jest.fn((input: Parameters<typeof fetch>[0], init?: RequestInit) => {
    if (init?.method !== 'PATCH') return original(input, init);
    state.requests += 1;
    if (reject) return Promise.reject(new TypeError('Network request failed'));
    return new Promise<Response>((resolve) => waiting.push(resolve));
  }) as unknown as typeof fetch;
  return {
    get requests() {
      return state.requests;
    },
    answer: (status: number, body: unknown) =>
      waiting.shift()?.(
        new Response(JSON.stringify(body), {
          status,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    restore: () => {
      globalThis.fetch = original;
    },
  };
}

function api(extra: Record<string, unknown> = {}) {
  return {
    [`GET ${BOOK}/publication-settings`]: publicationSettings(),
    [`GET ${BOOK}/settings/layout`]: layoutSettings(),
    [`PATCH ${BOOK}/settings`]: (request: FakeRequest) =>
      bookSettings({ id: 'book-1', ...(request.body as object) }),
    ...extra,
  };
}

describe('Style guide editor', () => {
  it('StyleGuideRow_WhenPressed_ShouldPushTheEditorUnderSettings', async () => {
    const app = await renderApp({
      url: '/book/book-1/settings',
      api: api({
        [`GET ${BOOK}/settings`]: bookSettings({
          id: 'book-1',
          styleGuide: 'Third person limited, past tense.',
        }),
      }),
    });

    const row = await screen.findByTestId(ids.row('style-guide'));
    expect(screen.getByTestId(`${ids.row('style-guide')}`)).toHaveTextContent(
      /Third person limited, past tense\./,
    );
    await fireEvent.press(row);

    expect(await screen.findByTestId(ids.styleGuideScreen)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/settings/style-guide');
    expect(screen.getByTestId(ids.styleGuideField).props.value).toBe(
      'Third person limited, past tense.',
    );
    expect(screen.getByTestId(shellTestIds.tab('settings'))).toBeSelected();
  });

  it('StyleGuideRow_WhenTheBookHasNoneButTheSeriesDoes_ShouldCarryTheSeriesChip', async () => {
    await renderApp({
      url: '/book/book-1/settings',
      api: api({
        [`GET ${BOOK}/settings`]: bookSettings({ id: 'book-1', seriesGuide: 'British spelling.' }),
      }),
    });

    await screen.findByTestId(ids.row('style-guide'));

    expect(screen.getByTestId(`${ids.row('style-guide')}-chip`)).toHaveTextContent('Series');
    expect(screen.getByTestId(ids.row('style-guide'))).toHaveTextContent(/British spelling\./);
  });

  it('Done_AfterTyping_ShouldPatchTheStyleGuideAndGoBackToSettings', async () => {
    const app = await renderApp({
      url: '/book/book-1/settings/style-guide',
      api: api({ [`GET ${BOOK}/settings`]: bookSettings({ id: 'book-1', styleGuide: 'Old.' }) }),
    });
    await screen.findByTestId(ids.styleGuideField);

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'No adverbs on tags.');
    expect(screen.getByTestId(ids.styleGuideWords)).toHaveTextContent('4 words');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-action`));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/settings'));
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`).map((c) => c.body)).toEqual([
      { styleGuide: 'No adverbs on tags.' },
    ]);
  });

  it('Back_AfterTyping_ShouldSaveBeforeLeaving', async () => {
    const app = await renderApp({ url: '/book/book-1/settings/style-guide', api: api() });
    await screen.findByTestId(ids.styleGuideField);

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Past tense.');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-back`));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/settings'));
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(1);
  });

  it('Done_WhenNothingChanged_ShouldGoBackWithoutSaving', async () => {
    const app = await renderApp({ url: '/book/book-1/settings/style-guide', api: api() });
    await screen.findByTestId(ids.styleGuideField);

    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-action`));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/settings'));
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(0);
  });

  it('Done_WhenTheServerRefuses_ShouldStayWithTheTextAndTheMessage', async () => {
    const app = await renderApp({
      url: '/book/book-1/settings/style-guide',
      api: api({
        [`PATCH ${BOOK}/settings`]: {
          status: 400,
          body: { error: 'The style guide is too long.', code: 'validation' },
        },
      }),
    });
    await screen.findByTestId(ids.styleGuideField);

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Long.');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-action`));

    expect(await screen.findByTestId(ids.styleGuideError)).toHaveTextContent(
      'The style guide is too long.',
    );
    expect(app.getPathname()).toBe('/book/book-1/settings/style-guide');
    expect(screen.getByTestId(ids.styleGuideField).props.value).toBe('Long.');
  });

  it('StyleGuide_InASeries_ShouldShowTheSeriesGuideReadOnly', async () => {
    await renderApp({
      url: '/book/book-1/settings/style-guide',
      api: api({
        [`GET ${BOOK}/settings`]: bookSettings({ id: 'book-1', seriesGuide: 'British spelling.' }),
      }),
    });

    expect(await screen.findByTestId(ids.styleGuideSeries)).toHaveTextContent(/British spelling\./);
    expect(screen.getByTestId(ids.styleGuideSeries)).toHaveTextContent(/Forsaken Gods/);
  });

  it('StyleGuide_ForAViewer_ShouldBeReadOnlyWithNoDone_WhileTheOwnerCanEdit', async () => {
    // Open through Settings so the header (the role) is loaded before the editor is looked at.
    const viewer = await renderApp({
      url: '/book/book-1/settings',
      api: api({ 'GET /api/books/book-1': bookHeader({ role: 'view' }) }),
    });
    await screen.findByTestId(ids.readOnlyNote);
    await fireEvent.press(screen.getByTestId(ids.row('style-guide')));

    expect(await screen.findByTestId(ids.styleGuideField)).toBeOnTheScreen();
    expect(screen.getByTestId(ids.styleGuideField).props.editable).toBe(false);
    expect(screen.queryByTestId(`${shellTestIds.pushBar}-action`)).toBeNull();
    expect(screen.queryByTestId(ids.styleGuideStatus)).toBeNull();
    expect(viewer.getPathname()).toBe('/book/book-1/settings/style-guide');
  });

  it('StyleGuide_ForTheOwner_ShouldBeEditableWithDone', async () => {
    await renderApp({ url: '/book/book-1/settings', api: api() });
    await screen.findByTestId(ids.row('title'));
    await waitFor(() => expect(screen.queryByTestId(ids.readOnlyNote)).toBeNull());
    await fireEvent.press(screen.getByTestId(ids.row('style-guide')));

    await screen.findByTestId(ids.styleGuideField);
    await waitFor(() => expect(screen.getByTestId(ids.styleGuideField).props.editable).toBe(true));
    expect(screen.getByTestId(`${shellTestIds.pushBar}-action`)).toBeOnTheScreen();
  });

  it('Autosave_ThreeSecondsAfterTyping_ShouldPatchWithoutDoneAndShowTheStatus', async () => {
    const app = await renderApp({ url: styleGuideUrl, api: api() });
    await screen.findByTestId(ids.styleGuideField);
    expect(screen.getByTestId(ids.styleGuideStatus)).toHaveTextContent('Saved');

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Past tense.');
    expect(screen.getByTestId(ids.styleGuideStatus)).toHaveTextContent('Unsaved');
    await act(async () => {
      jest.advanceTimersByTime(2900);
    });
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(0);
    await act(async () => {
      jest.advanceTimersByTime(100);
    });

    await waitFor(() =>
      expect(screen.getByTestId(ids.styleGuideStatus)).toHaveTextContent('Saved'),
    );
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`).map((c) => c.body)).toEqual([
      { styleGuide: 'Past tense.' },
    ]);
    expect(app.getPathname()).toBe(styleGuideUrl);
  });

  it('Done_WhileTheAutosaveIsInFlightAndFails_ShouldWaitAndStayWithTheMessage', async () => {
    const app = await renderApp({ url: styleGuideUrl, api: api() });
    await screen.findByTestId(ids.styleGuideField);
    const held = holdPatches();

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Long.');
    await act(async () => {
      jest.advanceTimersByTime(3000);
    });
    await waitFor(() => expect(held.requests).toBe(1));
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-action`));
    expect(app.getPathname()).toBe(styleGuideUrl);

    await act(async () => {
      held.answer(400, { error: 'The style guide is too long.', code: 'validation' });
    });

    expect(await screen.findByTestId(ids.styleGuideError)).toHaveTextContent(
      'The style guide is too long.',
    );
    expect(app.getPathname()).toBe(styleGuideUrl);
  });

  it('Done_WhileTheAutosaveIsInFlightAndSucceeds_ShouldGoBackAfterIt', async () => {
    const app = await renderApp({ url: styleGuideUrl, api: api() });
    await screen.findByTestId(ids.styleGuideField);
    const held = holdPatches();

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Fine.');
    await act(async () => {
      jest.advanceTimersByTime(3000);
    });
    await waitFor(() => expect(held.requests).toBe(1));
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-action`));
    expect(app.getPathname()).toBe(styleGuideUrl);

    await act(async () => {
      held.answer(200, bookSettings({ id: 'book-1', styleGuide: 'Fine.' }));
    });

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/settings'));
    expect(held.requests).toBe(1);
  });

  it('Back_WhenOffline_ShouldLeaveAndKeepTheEditQueuedUntilTheConnectionReturns', async () => {
    const app = await renderApp({ url: styleGuideUrl, api: api() });
    await screen.findByTestId(ids.styleGuideField);
    const offline = jest.spyOn(mobilePlatform, 'isOffline').mockReturnValue(true);
    const unsubscribe = jest.fn();
    let backOnline: () => void = () => undefined;
    jest.spyOn(mobilePlatform, 'onOnline').mockImplementation((listener) => {
      backOnline = listener;
      return unsubscribe;
    });
    const held = holdPatches({ reject: true });

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Queued.');
    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-back`));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/settings'));
    expect(saveStatusStore.getState()).toBe('offline');
    expect(unsubscribe).not.toHaveBeenCalled();

    offline.mockReturnValue(false);
    held.restore();
    await act(async () => {
      backOnline();
    });

    await waitFor(() => expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(1));
    expect(app.api.callsTo(`PATCH ${BOOK}/settings`)[0]?.body).toEqual({ styleGuide: 'Queued.' });
  });

  it('StyleGuide_WhenOffline_ShouldShowTheOfflineStatusInsteadOfAnError', async () => {
    await renderApp({ url: styleGuideUrl, api: api() });
    await screen.findByTestId(ids.styleGuideField);
    jest.spyOn(mobilePlatform, 'isOffline').mockReturnValue(true);
    jest.spyOn(mobilePlatform, 'onOnline').mockReturnValue(() => undefined);
    holdPatches({ reject: true });

    await fireEvent.changeText(screen.getByTestId(ids.styleGuideField), 'Queued.');
    await act(async () => {
      jest.advanceTimersByTime(3000);
    });

    await waitFor(() =>
      expect(screen.getByTestId(ids.styleGuideStatus)).toHaveTextContent(/Offline/),
    );
    expect(screen.queryByTestId(ids.styleGuideError)).toBeNull();
  });

  it('StyleGuide_WhenTheSettingsCannotBeLoaded_ShouldShowTheErrorAndRetry', async () => {
    await renderApp({
      url: '/book/book-1/settings/style-guide',
      api: api({
        [`GET ${BOOK}/settings`]: { status: 500, body: { error: 'Down.', code: 'internal_error' } },
      }),
    });

    expect(await screen.findByTestId(ids.error)).toHaveTextContent('Down.');
  });
});
