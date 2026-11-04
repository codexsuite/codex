import type { Beat, BookBeats } from '@codex/core/api/write';
import type { FakeHandler, FakeRequest } from '~/test-support/fakeApi';
import {
  FAKE_BOOK_ID,
  manuscript,
  manuscriptScene,
  outline,
  outlineAct,
  outlineChapter,
  outlineScene,
} from '~/test-support/fakes';

// A small book for the Write specs: Act "The Court" (chapters 1 and 2) and Act "Last Light" (chapter 3).

export const chapterScenes = {
  c1: ['s1', 's2'],
  c2: ['s3'],
  c3: ['s4'],
} as const;

export function writeOutline() {
  return outline({
    bookId: FAKE_BOOK_ID,
    acts: [
      outlineAct({
        id: 'act-1',
        title: 'The Court',
        chapters: [
          outlineChapter({
            id: 'c1',
            number: 1,
            title: 'An Empty Seat',
            scenes: [
              outlineScene({ id: 's1', number: '1.1', title: 'The doors', words: 3000 }),
              outlineScene({ id: 's2', number: '1.2', title: null, words: 2000 }),
            ],
          }),
          outlineChapter({
            id: 'c2',
            number: 2,
            title: 'The Bell',
            scenes: [outlineScene({ id: 's3', number: '2.1', words: 1500 })],
          }),
        ],
      }),
      outlineAct({
        id: 'act-2',
        title: 'Last Light',
        chapters: [
          outlineChapter({
            id: 'c3',
            number: 3,
            title: 'Grey Ink',
            scenes: [outlineScene({ id: 's4', number: '3.1', words: 800 })],
          }),
        ],
      }),
    ],
  });
}

export function writeManuscript() {
  return manuscript({
    bookId: FAKE_BOOK_ID,
    scenes: [
      manuscriptScene({ sceneId: 's1', body: 'First.\n', version: 1 }),
      manuscriptScene({ sceneId: 's2', body: 'Second.\n', version: 2 }),
      manuscriptScene({ sceneId: 's3', body: 'Third.\n', version: 3 }),
      manuscriptScene({ sceneId: 's4', body: 'Fourth.\n', version: 4 }),
    ],
  });
}

export function beat(overrides: Partial<Beat> = {}): Beat {
  return {
    id: 'beat-1',
    sceneId: 's1',
    body: 'Tobin counts the guards.',
    targetWordCount: 400,
    isHidden: false,
    contextEntryIds: [],
    title: '',
    ...overrides,
  };
}

/** `PUT /api/scenes/{id}/prose`: saves, and answers with the next version. */
const saveProse: FakeHandler = (request: FakeRequest) => ({
  version: (request.body as { version: number }).version + 1,
});

/** The routes Write reads and writes, on top of `worldRoutes()` (spread over it; override per spec). */
export function writeRoutes(
  beats: BookBeats = { bookId: FAKE_BOOK_ID, beats: [] },
): Record<string, FakeHandler> {
  return {
    [`GET /api/books/${FAKE_BOOK_ID}/outline`]: writeOutline(),
    [`GET /api/books/${FAKE_BOOK_ID}/manuscript`]: writeManuscript(),
    [`GET /api/books/${FAKE_BOOK_ID}/beats`]: beats,
    [`GET /api/books/${FAKE_BOOK_ID}/kb-additions`]: { bookId: FAKE_BOOK_ID, additions: [] },
    'PUT /api/scenes/s1/prose': saveProse,
    'PUT /api/scenes/s2/prose': saveProse,
    'PUT /api/scenes/s3/prose': saveProse,
    'PUT /api/scenes/s4/prose': saveProse,
  };
}
