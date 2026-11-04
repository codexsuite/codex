import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import { bookSwitcherLink } from '~/links';
import { worldRoutes } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { libraryTestIds as ids } from './testIds';
import { shelf } from './test-support/fakes';

const routes = () => ({ ...worldRoutes(), 'GET /api/library': shelf() });

async function openSwitcher(openBookId: string) {
  const app = await renderApp({ api: routes() });
  await fireEvent.press(await screen.findByTestId(ids.book(openBookId)));
  await screen.findByTestId('mobile-plan-screen');
  await act(async () => router.push(bookSwitcherLink(openBookId)));
  await screen.findByTestId(ids.switcherSheet);
  return app;
}

describe('Book switcher sheet', () => {
  it('ShouldListRecentBooksNewestFirstWithoutTheOpenOne', async () => {
    await openSwitcher('fg-1');

    await waitFor(() => expect(screen.getByTestId(ids.switcherBook('solo-1'))).toBeOnTheScreen());
    const listed = screen
      .getAllByTestId(/^mobile-library-switcher-book-/)
      .map((n) => n.props.testID);
    expect(listed).toEqual([
      ids.switcherBook('solo-1'),
      ids.switcherBook('fg-2'),
      ids.switcherBook('ns-1'),
    ]);
    expect(screen.getByTestId(ids.switcherBook('solo-1'))).toHaveTextContent(/850 words · 2h ago/);
    expect(screen.getByTestId(ids.switcherAll)).toHaveTextContent(/All books in Library/);
  });

  it('PickingAnotherBook_ShouldReplaceTheOpenBookAndBackShouldReachTheLibrary', async () => {
    const app = await openSwitcher('fg-1');

    await fireEvent.press(await screen.findByTestId(ids.switcherBook('ns-1')));

    await waitFor(() => expect(app.getPathname()).toBe('/book/ns-1/plan'));
    expect(screen.queryByTestId(ids.switcherSheet)).toBeNull();

    await act(async () => router.back());
    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(await screen.findByTestId(ids.list)).toBeOnTheScreen();
  });

  it('AllBooksInLibrary_ShouldReturnToTheLibraryWithTheBookClosed', async () => {
    const app = await openSwitcher('fg-1');

    await fireEvent.press(await screen.findByTestId(ids.switcherAll));

    await waitFor(() => expect(app.getPathname()).toBe('/'));
    expect(await screen.findByTestId(ids.list)).toBeOnTheScreen();
    expect(screen.queryByTestId('mobile-plan-screen')).toBeNull();
  });

  it('Close_ShouldReturnToTheBook', async () => {
    const app = await openSwitcher('fg-1');

    await userEvent.press(screen.getByTestId('mobile-shell-sheet-close'));

    await waitFor(() => expect(app.getPathname()).toBe('/book/fg-1/plan'));
  });

  it('WhenTheLibraryFailsToLoad_ShouldShowAnErrorThatRetries', async () => {
    const app = await renderApp({
      url: '/book/fg-1/plan',
      api: {
        ...routes(),
        'GET /api/library': {
          status: 500,
          body: { error: 'Library is down.', code: 'internal_error' },
        },
      },
    });
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.push(bookSwitcherLink('fg-1')));
    expect(await screen.findByTestId(ids.switcherError)).toHaveTextContent('Library is down.');

    app.api.respond('GET /api/library', shelf());
    await fireEvent.press(screen.getByTestId(`${ids.switcherError}-retry`));

    expect(await screen.findByTestId(ids.switcherBook('ns-1'))).toBeOnTheScreen();
  });

  it('WhileLoading_ShouldShowASpinnerAndStillOfferAllBooks', async () => {
    await renderApp({
      url: '/book/fg-1/plan',
      api: { ...routes(), 'GET /api/library': () => new Promise(() => undefined) },
    });
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.push(bookSwitcherLink('fg-1')));

    expect(await screen.findByTestId(ids.switcherLoading)).toBeOnTheScreen();
    expect(screen.getByTestId(ids.switcherAll)).toBeOnTheScreen();
  });
});
