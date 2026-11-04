import { act, fireEvent, screen } from '@testing-library/react-native';
import { router } from 'expo-router';
import { chapterJumpLink } from '~/links';
import { FAKE_BOOK_ID } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { writeTestIds } from './testIds';
import { writeRoutes } from './test-support/world';

async function openSheet(chapterId?: string, api: Record<string, unknown> = {}) {
  const app = await renderApp({ url: '/book/book-1/plan', api: { ...writeRoutes(), ...api } });
  await act(async () => router.push(chapterJumpLink('book-1', chapterId)));
  await screen.findByTestId(writeTestIds.chapterJumpSheet);
  return app;
}

describe('Chapter jump sheet', () => {
  it('ShouldListChaptersGroupedByActWithTheirCounts', async () => {
    await openSheet('c1');

    expect(await screen.findByTestId(writeTestIds.chapterJumpAct('act-1'))).toHaveTextContent(
      'The Court2',
    );
    expect(screen.getByTestId(writeTestIds.chapterJumpAct('act-2'))).toHaveTextContent(
      'Last Light1',
    );
    expect(screen.getByTestId(writeTestIds.chapterJumpRow('c1'))).toHaveTextContent(
      /^1 · An Empty Seat/,
    );
    expect(screen.getByTestId(`${writeTestIds.chapterJumpRow('c1')}-value`)).toHaveTextContent(
      '5,000 words',
    );
    expect(screen.getByTestId(writeTestIds.chapterJumpRow('c3'))).toHaveTextContent(
      /^3 · Grey Ink/,
    );
  });

  it('ShouldMarkOnlyTheChapterBeingRead', async () => {
    await openSheet('c2');

    expect(
      await screen.findByTestId(`${writeTestIds.chapterJumpRow('c2')}-chip`),
    ).toHaveTextContent('Reading');
    expect(screen.queryByTestId(`${writeTestIds.chapterJumpRow('c1')}-chip`)).toBeNull();
  });

  it('PickingAChapter_ShouldDismissAndOpenWriteAtIt', async () => {
    const app = await openSheet('c1');

    await fireEvent.press(await screen.findByTestId(writeTestIds.chapterJumpRow('c2')));

    expect(await screen.findByTestId(writeTestIds.screen)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/write');
    expect(app.getSearchParams()).toMatchObject({ chapterId: 'c2' });
  });

  it('ShouldNotListArchivedChaptersOrEmptyActs', async () => {
    const outline = {
      bookId: FAKE_BOOK_ID,
      acts: [
        {
          id: 'act-1',
          title: 'Only',
          position: 'a0',
          opener: null,
          chapters: [
            {
              id: 'old',
              number: null,
              title: 'Archived',
              slug: 'archived',
              position: 'a0',
              archived: true,
              archivedSceneCount: 1,
              scenes: [],
              opener: null,
            },
          ],
        },
      ],
    };
    await openSheet('c1', { [`GET /api/books/${FAKE_BOOK_ID}/outline`]: outline });

    expect(await screen.findByTestId(writeTestIds.chapterJumpEmpty)).toHaveTextContent(
      'This book has no chapters yet.',
    );
    expect(screen.queryByTestId(writeTestIds.chapterJumpRow('old'))).toBeNull();
  });

  it('WhenTheOutlineFails_ShouldShowTheErrorAndRetryLoadsTheChapters', async () => {
    const app = await openSheet('c1', {
      [`GET /api/books/${FAKE_BOOK_ID}/outline`]: {
        status: 500,
        body: { error: 'The outline is unavailable.', code: 'internal_error' },
      },
    });
    expect(await screen.findByTestId(writeTestIds.chapterJumpError)).toHaveTextContent(
      'The outline is unavailable.',
    );

    app.api.respond(
      `GET /api/books/${FAKE_BOOK_ID}/outline`,
      writeRoutes()[`GET /api/books/${FAKE_BOOK_ID}/outline`],
    );
    await fireEvent.press(screen.getByTestId(`${writeTestIds.chapterJumpError}-retry`));

    expect(await screen.findByTestId(writeTestIds.chapterJumpRow('c3'))).toBeOnTheScreen();
  });
});
