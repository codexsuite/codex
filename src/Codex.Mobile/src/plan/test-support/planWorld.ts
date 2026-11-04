import type { Label } from '@codex/core/api/structure';
import {
  kb,
  kbCategory,
  kbEntrySummary,
  outline,
  outlineAct,
  outlineChapter,
  outlineScene,
} from '~/test-support/fakes';

export const BOOK = 'book-1';

/** Three acts: two open, the third collapsed by default; an archived chapter that must not be drawn. */
export const planOutline = () =>
  outline({
    bookId: BOOK,
    acts: [
      outlineAct({
        id: 'act-1',
        title: 'The Court',
        chapters: [
          outlineChapter({
            id: 'chapter-1',
            number: 1,
            title: 'An Empty Seat',
            scenes: [
              outlineScene({
                id: 'scene-1',
                number: '1.1',
                title: 'The Hall',
                summary: 'Maren returns to the **Hollow Court**.',
                words: 3_000,
                labels: [
                  { id: 'label-1', name: 'drafting', color: 'info' },
                  { id: 'label-2', name: 'odd', color: '#abcdef' },
                ],
                linkedEntries: [{ id: 'entry-1', name: 'Ana' }],
              }),
              outlineScene({
                id: 'scene-2',
                number: '1.2',
                title: null,
                summary: null,
                words: 0,
              }),
            ],
          }),
          outlineChapter({
            id: 'chapter-old',
            number: null,
            title: 'Deleted Chapter',
            archived: true,
            scenes: [outlineScene({ id: 'scene-old', number: '9.1', title: 'Gone' })],
          }),
        ],
      }),
      outlineAct({
        id: 'act-2',
        title: 'The Litany',
        chapters: [
          outlineChapter({
            id: 'chapter-2',
            number: 2,
            title: 'The Bell',
            scenes: [outlineScene({ id: 'scene-3', number: '2.1', title: 'Matins', words: 1_000 })],
          }),
        ],
      }),
      outlineAct({
        id: 'act-3',
        title: 'Last Light',
        chapters: [
          outlineChapter({
            id: 'chapter-3',
            number: 3,
            title: 'The End',
            scenes: [outlineScene({ id: 'scene-4', number: '3.1', title: 'Dusk', words: 500 })],
          }),
        ],
      }),
    ],
  });

export const labels = (): Label[] => [
  { id: 'label-1', scopeId: BOOK, name: 'drafting', color: 'info', sceneCount: 1 },
  { id: 'label-3', scopeId: BOOK, name: 'revise', color: 'warn', sceneCount: 0 },
];

export const planKb = () =>
  kb({
    categories: [kbCategory({ id: 'category-1', tag: 'person' })],
    entries: [
      kbEntrySummary({ id: 'entry-1', name: 'Ana', categoryId: 'category-1' }),
      kbEntrySummary({
        id: 'entry-2',
        name: 'Tobin',
        categoryId: 'category-1',
        aliases: ['The Counter'],
      }),
    ],
  });

/** Routes on top of `worldRoutes()` for the Plan specs. */
export const planRoutes = () => ({
  [`GET /api/books/${BOOK}/outline`]: planOutline(),
  [`GET /api/books/${BOOK}/kb`]: planKb(),
  [`GET /api/books/${BOOK}/labels`]: labels(),
  [`GET /api/books/${BOOK}/kb-additions`]: {
    bookId: BOOK,
    additions: [
      { id: 'a1', sceneId: 'scene-1', entryId: 'entry-1', body: 'x' },
      { id: 'a2', sceneId: 'scene-1', entryId: 'entry-1', body: 'y' },
    ],
  },
});
