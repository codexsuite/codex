import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { router } from 'expo-router';
import { Keyboard, Platform } from 'react-native';
import {
  accountLink,
  bookLink,
  bookSwitcherLink,
  chapterJumpLink,
  entryLink,
  recordLink,
  sceneSheetLink,
  speechModelsLink,
  styleGuideLink,
  writeLink,
} from '~/links';
import { lastScreenMemory } from '~/book/lastScreen';
import { renderApp } from '~/test-support/renderScreen';
import { shellTestIds } from '~/ui';

describe('navigation skeleton', () => {
  it('Library_WhenABookIsTapped_ShouldOpenItOnPlanUnderTheBookBar', async () => {
    const app = await renderApp();

    await fireEvent.press(await screen.findByTestId('mobile-library-book-book-1'));

    expect(await screen.findByTestId('mobile-plan-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/plan');
    await waitFor(() =>
      expect(screen.getByTestId(`${shellTestIds.bookBar}-title`)).toHaveTextContent(
        'The Empty Seat',
      ),
    );
    expect(screen.getByTestId(`${shellTestIds.bookBar}-subtitle`)).toHaveTextContent(
      'Book 3 · Forsaken Gods',
    );
  });

  it('Tabs_WhenEachIsPressed_ShouldShowItsScreenAndKeepTheBookBar', async () => {
    const app = await renderApp({ url: '/book/book-1/plan' });
    await screen.findByTestId('mobile-plan-screen');

    const tabs = [
      ['write', 'mobile-write-screen'],
      ['knowledge', 'mobile-kb-screen'],
      ['settings', 'mobile-settings-screen'],
      ['plan', 'mobile-plan-screen'],
    ] as const;
    for (const [tab, screenId] of tabs) {
      await fireEvent.press(screen.getByTestId(shellTestIds.tab(tab)));
      expect(await screen.findByTestId(screenId)).toBeOnTheScreen();
      expect(app.getPathname()).toBe(`/book/book-1/${tab}`);
      expect(screen.getByTestId(shellTestIds.bookBar)).toBeOnTheScreen();
    }
  });

  it('PushedScreen_ShouldKeepItsTabSelectedAndShowAParentBar', async () => {
    const app = await renderApp({ url: '/book/book-1/knowledge/entry-1' });

    expect(await screen.findByTestId('mobile-kb-entry-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/knowledge/entry-1');
    expect(screen.getByTestId(shellTestIds.tab('knowledge'))).toBeSelected();
    expect(screen.getByTestId(`${shellTestIds.pushBar}-back`)).toBeOnTheScreen();
  });

  it('SceneSheet_FromWrite_ShouldOpenOverWriteAndCloseBackToIt', async () => {
    const app = await renderApp({ url: '/book/book-1/write' });
    await screen.findByTestId('mobile-write-screen');

    await act(async () => router.push(sceneSheetLink('book-1', 'scene-1')));

    expect(await screen.findByTestId('mobile-plan-scene-sheet')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/scene/scene-1');

    await fireEvent.press(screen.getByTestId(`${shellTestIds.sheet}-close`));
    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/write'));
  });

  it('BookLayout_WhenTheBookScreenChanges_ShouldRememberWhereYouWere', async () => {
    await renderApp({ url: '/book/book-1/knowledge/entry-1' });
    await screen.findByTestId('mobile-kb-entry-screen');

    expect(lastScreenMemory.lastBookId()).toBe('book-1');
    expect(lastScreenMemory.screenOf('book-1')).toEqual({ tab: 'knowledge', pushed: 'entry-1' });
  });

  it('RecordLink_FromTheSceneSheet_ShouldOpenTheRecordingSheetWithItsTarget', async () => {
    const app = await renderApp({ url: '/book/book-1/plan' });
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.push(sceneSheetLink('book-1', 'scene-1')));
    await screen.findByTestId('mobile-plan-scene-sheet');

    await act(async () => router.push(recordLink('book-1', { kind: 'scene', id: 'scene-1' })));

    expect(await screen.findByTestId('mobile-record-sheet')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/record');
    expect(app.getSearchParams()).toEqual({ bookId: 'book-1', target: 'scene:scene-1' });
  });

  it.each([
    [
      'writeLink with a scene',
      writeLink('book-1', { sceneId: 'scene-9' }),
      'mobile-write-screen',
      '/book/book-1/write',
    ],
    [
      'styleGuideLink',
      styleGuideLink('book-1'),
      'mobile-settings-style-guide-screen',
      '/book/book-1/settings/style-guide',
    ],
    ['accountLink', accountLink(), 'mobile-library-account-sheet', '/account'],
    [
      'bookSwitcherLink',
      bookSwitcherLink('book-1'),
      'mobile-library-book-switcher-sheet',
      '/book-switcher',
    ],
    ['speechModelsLink', speechModelsLink(), 'mobile-speech-screen', '/speech-models'],
  ])('%s_ShouldOpenItsRoute', async (_name, link, testId, pathname) => {
    const app = await renderApp();
    await screen.findByTestId('mobile-library-list');

    await act(async () => router.push(link));

    expect(await screen.findByTestId(testId)).toBeOnTheScreen();
    expect(app.getPathname()).toBe(pathname);
  });

  it('WriteLink_WithAScene_ShouldPassTheSceneToWrite', async () => {
    const app = await renderApp();
    await screen.findByTestId('mobile-library-list');

    await act(async () => router.push(writeLink('book-1', { sceneId: 'scene-9' })));

    expect(await screen.findByTestId('mobile-write-screen')).toBeOnTheScreen();
    expect(app.getSearchParams()).toEqual({ bookId: 'book-1', sceneId: 'scene-9' });
  });

  it('BookIndex_WhenOpenedOnItsOwn_ShouldRedirectToPlan', async () => {
    const app = await renderApp({ url: '/book/book-1' });

    expect(await screen.findByTestId('mobile-plan-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/plan');
  });

  it('TabBar_WhileTheKeyboardIsUp_ShouldBeHidden', async () => {
    const listeners = new Map<string, () => void>();
    jest.spyOn(Keyboard, 'addListener').mockImplementation((event, listener) => {
      listeners.set(event, listener as () => void);
      return { remove: () => void listeners.delete(event) };
    });
    const show = Platform.OS === 'ios' ? 'keyboardWillShow' : 'keyboardDidShow';
    const hide = Platform.OS === 'ios' ? 'keyboardWillHide' : 'keyboardDidHide';
    await renderApp({ url: '/book/book-1/write' });
    await screen.findByTestId('mobile-write-screen');
    expect(screen.getByTestId(shellTestIds.tabBar)).toBeOnTheScreen();

    await act(async () => listeners.get(show)?.());
    expect(screen.queryByTestId(shellTestIds.tabBar)).toBeNull();

    await act(async () => listeners.get(hide)?.());
    expect(screen.getByTestId(shellTestIds.tabBar)).toBeOnTheScreen();
  });

  it('PushedScreen_FromAColdLink_ShouldGoBackToItsTabList', async () => {
    const app = await renderApp({ url: '/book/book-1/knowledge/entry-1' });
    await screen.findByTestId('mobile-kb-entry-screen');

    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-back`));

    expect(await screen.findByTestId('mobile-kb-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/knowledge');
  });

  it('StyleGuide_FromAColdLink_ShouldGoBackToSettings', async () => {
    const app = await renderApp({ url: '/book/book-1/settings/style-guide' });
    await screen.findByTestId('mobile-settings-style-guide-screen');

    await fireEvent.press(screen.getByTestId(`${shellTestIds.pushBar}-back`));

    expect(await screen.findByTestId('mobile-settings-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/settings');
  });

  it('Tab_WhenPressedWhileSelectedOnAPushedScreen_ShouldPopToItsTop', async () => {
    const app = await renderApp({ url: '/book/book-1/knowledge' });
    await screen.findByTestId('mobile-kb-screen');
    await act(async () => router.push(entryLink('book-1', 'entry-1')));
    await screen.findByTestId('mobile-kb-entry-screen');

    await fireEvent.press(screen.getByTestId(shellTestIds.tab('knowledge')));

    expect(await screen.findByTestId('mobile-kb-screen')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/knowledge');
  });

  it('ChapterJumpSheet_PickingAChapter_ShouldDismissAndTellWriteWhichChapter', async () => {
    const app = await renderApp({ url: '/book/book-1/write' });
    await screen.findByTestId('mobile-write-screen');
    await act(async () => router.push(chapterJumpLink('book-1', 'chapter-1')));
    expect(await screen.findByTestId('mobile-write-chapter-jump-sheet')).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/chapters');

    await act(async () => {
      router.dismiss();
      router.navigate(writeLink('book-1', { chapterId: 'chapter-2' }));
    });

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/write'));
    expect(screen.getByTestId('mobile-write-screen')).toBeOnTheScreen();
    expect(app.getSearchParams()).toEqual({ bookId: 'book-1', chapterId: 'chapter-2' });
  });

  it('Write_WhenLinkedToTwiceWithDifferentScenes_ShouldKeepOneScreenWithNewParams', async () => {
    const app = await renderApp({ url: '/book/book-1/plan' });
    await screen.findByTestId('mobile-plan-screen');

    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's1' })));
    await screen.findByTestId('mobile-write-screen');
    await act(async () => router.navigate(writeLink('book-1', { sceneId: 's2' })));

    await waitFor(() => expect(app.getSearchParams()).toEqual({ bookId: 'book-1', sceneId: 's2' }));
    expect(screen.getAllByTestId('mobile-write-screen')).toHaveLength(1);
  });

  it('BookSwitcher_PickingAnotherBook_ShouldReplaceTheOpenBookAndBackShouldReachTheLibrary', async () => {
    const app = await renderApp();
    await fireEvent.press(await screen.findByTestId('mobile-library-book-book-1'));
    await screen.findByTestId('mobile-plan-screen');
    await act(async () => router.push(bookSwitcherLink('book-1')));
    await screen.findByTestId('mobile-library-book-switcher-sheet');

    await act(async () => {
      router.dismiss();
      router.replace(bookLink('book-2'));
    });

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-2/plan'));
    expect(await screen.findByTestId('mobile-plan-screen')).toBeOnTheScreen();

    await act(async () => router.back());
    await waitFor(() => expect(app.getPathname()).toBe('/'));
  });
});
