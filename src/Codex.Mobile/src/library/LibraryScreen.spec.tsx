import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { platform } from '@codex/core/platform';
import { router } from 'expo-router';
import { bookLink, libraryLink } from '~/links';
import { lastScreenMemory } from '~/book/lastScreen';
import { worldRoutes } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { libraryTestIds as ids } from './testIds';
import { me, shelf } from './test-support/fakes';

const routes = () => ({
  ...worldRoutes(),
  'GET /api/library': shelf(),
  'GET /api/auth/me': me(),
});

beforeEach(() => {
  const store = platform().persistentStore();
  store.removeItem('codex.last-book');
  for (const id of ['fg-1', 'fg-2', 'ns-1', 'solo-1', 'deleted-book'])
    store.removeItem(`codex.last.${id}`);
});

describe('Library', () => {
  it('ShouldShowSeriesGroupsInOrderThenStandalone', async () => {
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);

    expect(screen.getByTestId(ids.title)).toHaveTextContent('Library');
    expect(screen.getByTestId(ids.sectionHeader('series-fg'))).toHaveTextContent(/Forsaken Gods2$/);
    expect(screen.getByTestId(ids.sectionHeader('series-ns'))).toHaveTextContent(/Night Shift1$/);
    expect(screen.getByTestId(ids.sectionHeader('standalone'))).toHaveTextContent(/Standalone/);
    expect(screen.getAllByTestId(/^mobile-library-group-/).map((n) => n.props.testID)).toEqual([
      ids.sectionHeader('series-fg'),
      ids.sectionHeader('series-ns'),
      ids.sectionHeader('standalone'),
    ]);
  });

  it('ShouldDrawARowWithOverlineTitleWordsEditedAndStatus', async () => {
    await renderApp({ api: routes() });
    const row = await screen.findByTestId(ids.book('fg-1'));

    expect(within(row).getByText('Book 001')).toBeOnTheScreen();
    expect(row).toHaveTextContent(/The Empty Seat/);
    expect(within(row).getByText('98.4k / 120k words · Yesterday')).toBeOnTheScreen();
    expect(within(row).getByText('Revising')).toBeOnTheScreen();
  });

  it('ShouldDrawAStandaloneRowWithoutAnOverlineOrTarget', async () => {
    await renderApp({ api: routes() });
    const row = await screen.findByTestId(ids.book('solo-1'));

    expect(within(row).queryByText(/^Book \d{3}$/)).toBeNull();
    expect(within(row).getByText('850 words · 2h ago')).toBeOnTheScreen();
    expect(within(row).getByText('Needs check')).toBeOnTheScreen();
  });

  it('WhenARowIsTapped_ShouldOpenTheBookOnPlan', async () => {
    const app = await renderApp({ api: routes() });

    await fireEvent.press(await screen.findByTestId(ids.book('ns-1')));

    await waitFor(() => expect(app.getPathname()).toBe('/book/ns-1/plan'));
  });

  it('WhenSearching_ShouldFilterRowsByTitleAndSeries', async () => {
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);

    await userEvent.type(screen.getByTestId(ids.search), 'hollow');
    expect(screen.queryByTestId(ids.book('fg-1'))).toBeNull();
    expect(screen.getByTestId(ids.book('fg-2'))).toBeOnTheScreen();
    expect(screen.queryByTestId(ids.sectionHeader('series-ns'))).toBeNull();

    await fireEvent.changeText(screen.getByTestId(ids.search), 'night shift');
    expect(screen.getByTestId(ids.book('ns-1'))).toBeOnTheScreen();
    expect(screen.queryByTestId(ids.book('fg-2'))).toBeNull();
  });

  it('WhenNothingMatches_ShouldSayNoMatchesAndNotEmptyLibrary', async () => {
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);

    await fireEvent.changeText(screen.getByTestId(ids.search), 'zzzz');

    expect(await screen.findByTestId(ids.noMatches)).toBeOnTheScreen();
    expect(screen.queryByTestId(ids.empty)).toBeNull();
  });

  it('WhenTheLibraryIsEmpty_ShouldShowTheEmptyState', async () => {
    await renderApp({ api: { ...routes(), 'GET /api/library': { series: [], standalone: [] } } });

    expect(await screen.findByTestId(ids.empty)).toBeOnTheScreen();
  });

  it('WhileLoading_ShouldShowTheSpinner', async () => {
    await renderApp({
      api: { ...routes(), 'GET /api/library': () => new Promise(() => undefined) },
    });

    expect(await screen.findByTestId(ids.loading)).toBeOnTheScreen();
  });

  it('WhenLoadingFails_ShouldShowTheErrorAndRetryReloads', async () => {
    const app = await renderApp({
      api: {
        ...routes(),
        'GET /api/library': {
          status: 500,
          body: { error: 'Library is down.', code: 'internal_error' },
        },
      },
    });
    expect(await screen.findByTestId(ids.error)).toHaveTextContent('Library is down.');

    app.api.respond('GET /api/library', shelf());
    await fireEvent.press(screen.getByTestId(`${ids.error}-retry`));

    expect(await screen.findByTestId(ids.book('fg-1'))).toBeOnTheScreen();
    expect(app.api.callsTo('GET /api/library')).toHaveLength(2);
  });

  it('PullingToRefresh_ShouldReloadTheLibrary', async () => {
    const app = await renderApp({ api: routes() });
    const list = await screen.findByTestId(ids.list);

    await act(async () => {
      const refresh = list.props.refreshControl as { props: { onRefresh: () => void } };
      refresh.props.onRefresh();
    });

    await waitFor(() => expect(app.api.callsTo('GET /api/library')).toHaveLength(2));
  });

  it('ShouldEndWithTheDesktopNote', async () => {
    await renderApp({ api: routes() });

    expect(await screen.findByTestId(ids.footer)).toHaveTextContent(
      'Import and series settings are on desktop.',
    );
  });

  it('WhenARefreshFails_ShouldKeepTheRowsAndShowANotice', async () => {
    const app = await renderApp({ api: routes() });
    const list = await screen.findByTestId(ids.list);
    app.api.respond('GET /api/library', {
      status: 500,
      body: { error: 'Library is down.', code: 'internal_error' },
    });

    await act(async () => {
      const refresh = list.props.refreshControl as { props: { onRefresh: () => void } };
      refresh.props.onRefresh();
    });

    expect(await screen.findByTestId(ids.refreshFailed)).toBeOnTheScreen();
    expect(screen.getByTestId(ids.book('fg-1'))).toBeOnTheScreen();
    expect(screen.queryByTestId(ids.error)).toBeNull();
  });

  it('WhenReturningFromABookAndTheBooksAreStale_ShouldReloadThem', async () => {
    const app = await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);
    expect(app.api.callsTo('GET /api/library')).toHaveLength(1);

    await act(async () => router.push(bookLink('ns-1')));
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.dismissTo(libraryLink()));

    await waitFor(() => expect(app.api.callsTo('GET /api/library')).toHaveLength(2));
  });

  it('ShouldShowTheInitialsAndTheAvatarOpensTheAccountSheet', async () => {
    const app = await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);
    await waitFor(() => expect(screen.getByTestId(ids.avatar)).toHaveTextContent('JD'));

    await fireEvent.press(screen.getByTestId(ids.avatar));

    expect(await screen.findByTestId(ids.accountSheet)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/account');
  });
});

describe('Library Continue card', () => {
  it('WhenNoBookWasOpened_ShouldHideIt', async () => {
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);

    expect(screen.queryByTestId(ids.continueCard)).toBeNull();
  });

  it('WhenTheLastBookIsGone_ShouldHideIt', async () => {
    lastScreenMemory.remember('deleted-book', { tab: 'write', pushed: null });
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);

    expect(screen.queryByTestId(ids.continueCard)).toBeNull();
  });

  it('ShouldShowTheLastBookAndWhereItWasLeftAndOpenThatScreen', async () => {
    lastScreenMemory.remember('fg-1', { tab: 'knowledge', pushed: 'entry-1' });
    const app = await renderApp({ api: routes() });
    const card = await screen.findByTestId(ids.continueCard);

    expect(within(card).getByText('Knowledge')).toBeOnTheScreen();
    expect(card).toHaveTextContent(/The Empty Seat/);
    expect(within(card).getByText('98.4k / 120k words · Yesterday')).toBeOnTheScreen();

    await fireEvent.press(card);

    await waitFor(() => expect(app.getPathname()).toBe('/book/fg-1/knowledge/entry-1'));
  });

  it('WhenTheBookHasNoRememberedScreen_ShouldOpenPlan', async () => {
    platform().persistentStore().setItem('codex.last-book', 'ns-1');
    expect(lastScreenMemory.screenOf('ns-1')).toBeNull();
    const app = await renderApp({ api: routes() });

    await fireEvent.press(await screen.findByTestId(ids.continueCard));

    await waitFor(() => expect(app.getPathname()).toBe('/book/ns-1/plan'));
  });

  it('WhileSearching_ShouldHideIt', async () => {
    lastScreenMemory.remember('fg-1', { tab: 'write', pushed: null });
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.continueCard);

    await fireEvent.changeText(screen.getByTestId(ids.search), 'north');

    expect(screen.queryByTestId(ids.continueCard)).toBeNull();
  });

  it('WhenReturningFromABook_ShouldShowTheBookJustOpened', async () => {
    await renderApp({ api: routes() });
    await screen.findByTestId(ids.list);
    expect(screen.queryByTestId(ids.continueCard)).toBeNull();

    await act(async () => router.push(bookLink('ns-1')));
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.dismissTo(libraryLink()));

    const card = await screen.findByTestId(ids.continueCard);
    expect(card).toHaveTextContent(/Graveyard Hours/);
  });
});
