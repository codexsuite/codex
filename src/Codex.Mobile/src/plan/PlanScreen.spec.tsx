import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { router } from 'expo-router';
import { renderApp } from '~/test-support/renderScreen';
import {
  bookHeader,
  outline,
  outlineAct,
  outlineChapter,
  outlineScene,
} from '~/test-support/fakes';
import { BOOK, planRoutes } from './test-support/planWorld';
import { planTestIds as ids } from './testIds';
import { shellTestIds } from '~/ui';
import { readCollapsed, writeCollapsed } from './collapsedActs';

const open = async (api: Record<string, unknown> = {}) => {
  const app = await renderApp({
    url: `/book/${BOOK}/plan`,
    api: { ...planRoutes(), ...api },
  });
  await screen.findByTestId(ids.scene('scene-1'));
  return app;
};

describe('Plan outline', () => {
  // The device store outlives a test: start every test with nothing remembered.
  beforeEach(() => writeCollapsed(BOOK, {}));

  it('Cards_ShouldShowNumberTitleSummaryLabelsEntriesAdditionsAndWords', async () => {
    await open();

    expect(screen.getByTestId(ids.sceneTitle('scene-1'))).toHaveTextContent(/The Hall/);
    // Markdown marks are not shown in the two-line summary.
    expect(screen.getByTestId(ids.sceneSummary('scene-1'))).toHaveTextContent(
      'Maren returns to the Hollow Court.',
    );
    expect(screen.getByTestId(ids.sceneLabel('scene-1', 'label-1'))).toHaveTextContent(/drafting/);
    expect(screen.getByTestId(ids.sceneLabel('scene-1', 'label-2'))).toHaveTextContent(/odd/);
    expect(screen.getByTestId(ids.sceneEntry('scene-1', 'entry-1'))).toHaveTextContent(/Ana/);
    await waitFor(() =>
      expect(screen.getByTestId(ids.sceneAdditions('scene-1'))).toHaveTextContent(/\+2 additions/),
    );
    expect(screen.getByTestId(ids.sceneWords('scene-1'))).toHaveTextContent(/3,000 words/);
    expect(within(screen.getByTestId(ids.scene('scene-1'))).getByText('1')).toBeOnTheScreen();
  });

  it('Card_WhenTheSceneHasNoTitleOrSummary_ShouldShowUntitledAndNotStarted', async () => {
    await open();

    expect(screen.getByTestId(ids.sceneTitle('scene-2'))).toHaveTextContent(/Untitled/);
    expect(screen.getByTestId(ids.sceneTitle('scene-2'))).toHaveStyle({ fontStyle: 'italic' });
    expect(screen.queryByTestId(ids.sceneSummary('scene-2'))).toBeNull();
    expect(screen.getByTestId(ids.sceneWords('scene-2'))).toHaveTextContent(/Not started/);
    expect(screen.queryByTestId(ids.sceneAdditions('scene-2'))).toBeNull();
  });

  it('Acts_ShouldListChaptersAsHeadingsAndLeaveOutArchivedChapters', async () => {
    await open();

    expect(screen.getByTestId(ids.act('act-1'))).toHaveTextContent(/Act 1/);
    expect(screen.getByTestId(ids.act('act-1'))).toHaveTextContent(/The Court/);
    expect(screen.getByTestId(ids.chapter('chapter-1'))).toHaveTextContent(/Ch 1/);
    expect(screen.getByTestId(ids.chapter('chapter-1'))).toHaveTextContent(/An Empty Seat/);
    expect(screen.getByTestId(ids.chapter('chapter-1'))).toHaveTextContent(/2 scenes/);
    expect(screen.queryByTestId(ids.chapter('chapter-old'))).toBeNull();
    expect(screen.queryByTestId(ids.scene('scene-old'))).toBeNull();
  });

  it('Counts_ShouldShowWordsPagesAndReadTimeOfTheWholeBookFromServerWords', async () => {
    await open();

    // 3,000 + 0 + 1,000 + 500 words; 250 words a page, 238 words a minute.
    expect(screen.getByTestId(ids.counts)).toHaveTextContent(/4,500 words · 18 pages · 19m read/);
    expect(screen.getByTestId(ids.scopePill)).toHaveTextContent(/Full manuscript/);
  });

  it('ActHeader_WhenPressed_ShouldCollapseItsChaptersAndRememberItForTheBook', async () => {
    await open();

    await fireEvent.press(screen.getByTestId(ids.act('act-1')));

    expect(screen.queryByTestId(ids.scene('scene-1'))).toBeNull();
    expect(screen.queryByTestId(ids.chapter('chapter-1'))).toBeNull();
    expect(screen.getByTestId(ids.act('act-1'))).toBeCollapsed();
    expect(readCollapsed(BOOK)).toEqual({ 'act-1': true });
    await fireEvent.press(screen.getByTestId(ids.act('act-1')));
    expect(await screen.findByTestId(ids.scene('scene-1'))).toBeOnTheScreen();
  });

  it('ThirdAct_WhenNeverToggled_ShouldStartCollapsedAndOpenWhenPressed', async () => {
    await open();

    expect(screen.getByTestId(ids.act('act-3'))).toBeCollapsed();
    expect(screen.queryByTestId(ids.scene('scene-4'))).toBeNull();

    // The list draws a screenful first: fold the open acts so the third act's card is inside that window.
    await fireEvent.press(screen.getByTestId(ids.act('act-1')));
    await fireEvent.press(screen.getByTestId(ids.act('act-2')));
    await fireEvent.press(screen.getByTestId(ids.act('act-3')));

    expect(await screen.findByTestId(ids.scene('scene-4'))).toBeOnTheScreen();
    expect(screen.getByTestId(ids.act('act-3'))).toBeExpanded();
  });

  it('Card_WhenPressed_ShouldOpenTheSceneSheetOverPlan', async () => {
    const app = await open();

    await fireEvent.press(screen.getByTestId(ids.scene('scene-2')));

    expect(await screen.findByTestId(ids.sheet)).toBeOnTheScreen();
    expect(app.getPathname()).toBe(`/book/${BOOK}/scene/scene-2`);
  });

  it('Scope_WhenAnActIsPicked_ShouldNarrowTheListAndTheCounts', async () => {
    await open();

    await fireEvent.press(screen.getByTestId(ids.scopePill));
    await fireEvent.press(screen.getByTestId(ids.scopeOption('act:act-2')));

    expect(screen.queryByTestId(ids.act('act-1'))).toBeNull();
    expect(screen.getByTestId(ids.act('act-2'))).toHaveTextContent(/Act 2/);
    expect(screen.getByTestId(ids.scene('scene-3'))).toBeOnTheScreen();
    expect(screen.getByTestId(ids.counts)).toHaveTextContent(/1,000 words/);
    expect(screen.getByTestId(ids.scopePill)).toHaveTextContent(/Act 2: The Litany/);
    expect(screen.queryByTestId(ids.scopeList)).toBeNull();
  });

  it('Scope_WhenAChapterIsPicked_ShouldShowOnlyThatChaptersScenes', async () => {
    await open();

    await fireEvent.press(screen.getByTestId(ids.scopePill));
    await fireEvent.press(screen.getByTestId(ids.scopeOption('chapter:chapter-1')));

    expect(screen.getByTestId(ids.scene('scene-1'))).toBeOnTheScreen();
    expect(screen.getByTestId(ids.scene('scene-2'))).toBeOnTheScreen();
    expect(screen.queryByTestId(ids.act('act-2'))).toBeNull();
    expect(screen.getByTestId(ids.counts)).toHaveTextContent(/3,000 words/);
  });

  it('Book_WithNoChapters_ShouldSayNoChaptersYet', async () => {
    await renderApp({
      url: `/book/${BOOK}/plan`,
      api: {
        ...planRoutes(),
        [`GET /api/books/${BOOK}/outline`]: outline({
          bookId: BOOK,
          acts: [outlineAct({ id: 'act-x', title: '', chapters: [] })],
        }),
      },
    });

    expect(await screen.findByTestId(ids.empty)).toHaveTextContent(/No chapters yet\./);
    expect(screen.getByTestId(ids.act('act-x'))).toHaveTextContent(/Untitled act/);
  });

  it('Book_WithNoActs_ShouldSayNoChaptersYet', async () => {
    await renderApp({
      url: `/book/${BOOK}/plan`,
      api: {
        ...planRoutes(),
        [`GET /api/books/${BOOK}/outline`]: outline({ bookId: BOOK, acts: [] }),
      },
    });

    expect(await screen.findByTestId(ids.empty)).toHaveTextContent(/No chapters yet\./);
  });

  it('Outline_WhileLoading_ShouldShowASpinner', async () => {
    await renderApp({
      url: `/book/${BOOK}/plan`,
      api: {
        ...planRoutes(),
        [`GET /api/books/${BOOK}/outline`]: () => new Promise(() => undefined),
      },
    });

    expect(await screen.findByTestId(ids.loading)).toBeOnTheScreen();
  });

  it('Outline_WhenItFailsToLoad_ShouldShowTheErrorAndRetryReloadsIt', async () => {
    let fail = true;
    const app = await renderApp({
      url: `/book/${BOOK}/plan`,
      api: {
        ...planRoutes(),
        [`GET /api/books/${BOOK}/outline`]: () =>
          fail
            ? { status: 500, body: { error: 'The outline is down.', code: 'internal_error' } }
            : planRoutes()[`GET /api/books/${BOOK}/outline`],
      },
    });
    expect(await screen.findByTestId(ids.error)).toHaveTextContent(/The outline is down\./);

    fail = false;
    await fireEvent.press(screen.getByTestId(`${ids.error}-retry`));

    expect(await screen.findByTestId(ids.scene('scene-1'))).toBeOnTheScreen();
    expect(app.api.callsTo(`GET /api/books/${BOOK}/outline`).length).toBe(2);
  });

  it('PullToRefresh_ShouldReloadTheOutline', async () => {
    const app = await open();
    const list = screen.getByTestId(ids.list);

    await act(async () => list.props.refreshControl.props.onRefresh());

    await waitFor(() => expect(app.api.callsTo(`GET /api/books/${BOOK}/outline`).length).toBe(2));
    await waitFor(() => expect(app.api.callsTo(`GET /api/books/${BOOK}/kb`).length).toBe(2));
    await waitFor(() =>
      expect(app.api.callsTo(`GET /api/books/${BOOK}/kb-additions`).length).toBe(2),
    );
  });

  it('Screen_ShouldKeepTheBookBarAndTabBarAboveAndBelow', async () => {
    await open();

    expect(screen.getByTestId(shellTestIds.bookBar)).toBeOnTheScreen();
    expect(screen.getByTestId(shellTestIds.tab('plan'))).toBeSelected();
  });

  it('SceneSheetLink_FromThePlanStack_ShouldNotLeaveThePlanTab', async () => {
    const app = await open();
    await act(async () =>
      router.push({
        pathname: '/book/[bookId]/scene/[sceneId]',
        params: { bookId: BOOK, sceneId: 'scene-1' },
      }),
    );

    expect(await screen.findByTestId(ids.sheet)).toBeOnTheScreen();
    expect(app.getPathname()).toBe(`/book/${BOOK}/scene/scene-1`);
  });
});

describe('New chapter', () => {
  beforeEach(() => writeCollapsed(BOOK, {}));

  // The outline answers with the new chapter and scene once both have been created.
  const growingApi = () => {
    let created = false;
    return {
      ...planRoutes(),
      [`GET /api/books/${BOOK}/outline`]: () => {
        const o = planRoutes()[`GET /api/books/${BOOK}/outline`];
        if (created) {
          o.acts[1]!.chapters.push(
            outlineChapter({
              id: 'chapter-new',
              number: 3,
              title: 'New chapter',
              scenes: [outlineScene({ id: 'scene-new', number: '3.1', title: null, words: 0 })],
            }),
          );
        }
        return o;
      },
      'POST /api/acts/act-2/chapters': () => {
        created = true;
        return outlineChapter({ id: 'chapter-new', title: 'New chapter', scenes: [] });
      },
      'POST /api/chapters/chapter-new/scenes': outlineScene({ id: 'scene-new', number: '3.1' }),
    };
  };

  it('Button_ShouldEndEachOpenActAndNotACollapsedOne', async () => {
    await open();

    expect(screen.getByTestId(ids.newChapter('act-1'))).toHaveTextContent(/\+ New chapter/);
    expect(screen.getByTestId(ids.newChapter('act-2'))).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId(ids.act('act-1')));
    expect(screen.queryByTestId(ids.newChapter('act-1'))).toBeNull();
  });

  it('Button_WhenPressed_ShouldAddAChapterAfterTheLastOneThenAScene_AndOpenItsSheet', async () => {
    const app = await open(growingApi());

    await fireEvent.press(screen.getByTestId(ids.newChapter('act-2')));

    expect(await screen.findByTestId(ids.sheetTitle)).toBeOnTheScreen();
    expect(app.getPathname()).toBe(`/book/${BOOK}/scene/scene-new`);
    expect(app.api.callsTo('POST /api/acts/act-2/chapters')[0]?.body).toEqual({
      title: null,
      afterId: 'chapter-2',
    });
    expect(app.api.callsTo('POST /api/chapters/chapter-new/scenes')[0]?.body).toEqual({
      title: null,
      afterId: null,
    });
    expect(app.api.callsTo(`GET /api/books/${BOOK}/outline`).length).toBeGreaterThan(1);
  });

  it('Button_InAnActEndingInAnArchivedChapter_ShouldAddAfterTheLastLiveChapter', async () => {
    const app = await open(growingApi());

    await fireEvent.press(screen.getByTestId(ids.newChapter('act-1')));

    await waitFor(() =>
      expect(app.api.callsTo('POST /api/acts/act-1/chapters')[0]?.body).toEqual({
        title: null,
        afterId: 'chapter-1',
      }),
    );
  });

  it('Button_WhenTheServerRefuses_ShouldSayWhyAndStayOnPlan', async () => {
    const app = await open({
      'POST /api/acts/act-2/chapters': {
        status: 403,
        body: { error: 'You can only view this book.', code: 'forbidden' },
      },
    });

    await fireEvent.press(screen.getByTestId(ids.newChapter('act-2')));

    expect(await screen.findByTestId(ids.newChapterError)).toHaveTextContent(
      /You can only view this book\./,
    );
    expect(app.getPathname()).toBe(`/book/${BOOK}/plan`);
  });

  it('Button_ForAnEmptyBook_ShouldSitUnderNoChaptersYet', async () => {
    const app = await renderApp({
      url: `/book/${BOOK}/plan`,
      api: {
        ...planRoutes(),
        [`GET /api/books/${BOOK}/outline`]: outline({
          bookId: BOOK,
          acts: [outlineAct({ id: 'act-x', title: '', chapters: [] })],
        }),
        'POST /api/acts/act-x/chapters': () => ({
          status: 500,
          body: { error: 'Stop.', code: 'internal_error' },
        }),
      },
    });
    await screen.findByTestId(ids.empty);

    await fireEvent.press(screen.getByTestId(ids.newChapter('act-x')));

    await waitFor(() =>
      expect(app.api.callsTo('POST /api/acts/act-x/chapters')[0]?.body).toEqual({
        title: null,
        afterId: null,
      }),
    );
  });

  it('Button_ForAReadOnlyRole_ShouldNotBeThere', async () => {
    await open({ [`GET /api/books/${BOOK}`]: bookHeader({ id: BOOK, role: 'comment' }) });

    await waitFor(() => expect(screen.queryByTestId(ids.newChapter('act-1'))).toBeNull());
    expect(screen.queryByTestId(ids.newChapter('act-2'))).toBeNull();
  });

  it('Button_UnderAChapterScope_ShouldNotBeThere', async () => {
    await open();

    await fireEvent.press(screen.getByTestId(ids.scopePill));
    await fireEvent.press(screen.getByTestId(ids.scopeOption('chapter:chapter-1')));

    expect(screen.queryByTestId(ids.newChapter('act-1'))).toBeNull();
  });
});
