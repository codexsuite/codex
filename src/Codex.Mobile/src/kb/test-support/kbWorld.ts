import type { Kb } from '@codex/core/api/kb';
import type { BookKbAdditions } from '@codex/core/api/write';
import { kbCategory, kbEntry, kbEntrySummary } from '~/test-support/fakes';

// A Knowledge base for specs: a series book with one shared category (Rules), and the book's Characters and Places.
export const seriesKb: Kb = {
  categories: [
    kbCategory({
      id: 'rules',
      name: 'Rules',
      colour: 'category-law',
      position: 'a0',
      fromSeries: true,
      seriesName: 'The Ashen Crown',
    }),
    kbCategory({ id: 'people', name: 'Characters', colour: 'category-person', position: 'b0' }),
    kbCategory({ id: 'places', name: 'Places', colour: 'category-story', position: 'c0' }),
  ],
  entries: [
    kbEntrySummary({
      id: 'oath',
      categoryId: 'rules',
      name: 'The Oath',
      fromSeries: true,
      seriesName: 'The Ashen Crown',
      sceneCount: 4,
    }),
    kbEntrySummary({
      id: 'maren',
      categoryId: 'people',
      name: 'Maren',
      aliases: ['The Queen'],
      alwaysInContext: true,
      summaryLine: 'Rules from the north.',
      sceneCount: 2,
    }),
    kbEntrySummary({ id: 'tobin', categoryId: 'people', name: 'Tobin', sceneCount: 0 }),
    kbEntrySummary({ id: 'hall', categoryId: 'places', name: 'The Hall', sceneCount: 1 }),
  ],
};

export const marenEntry = kbEntry({
  id: 'maren',
  bookId: 'book-1',
  categoryId: 'people',
  categoryName: 'Characters',
  categoryColour: 'category-person',
  name: 'Maren',
  summary: 'Rules from the north.',
  aliases: ['The Queen'],
  alwaysInContext: false,
  usedIn: [
    {
      sceneId: 'scene-1',
      bookId: 'book-1',
      chapterNumber: 1,
      chapterTitle: 'An Empty Seat',
      number: '1.1',
      kinds: [{ kind: 'mention', count: 2 }],
      snippet: 'Maren does not come to the hall.',
    },
    {
      sceneId: 'scene-9',
      bookId: 'book-0',
      chapterNumber: 3,
      chapterTitle: 'Another Book',
      number: '3.2',
      kinds: [],
      snippet: 'Not in this book.',
    },
  ],
});

export const marenAdditions: BookKbAdditions = {
  bookId: 'book-1',
  additions: [
    { id: 'add-1', sceneId: 'scene-1', entryId: 'maren', body: 'Lies with a level voice.' },
    { id: 'add-2', sceneId: 'scene-1', entryId: 'tobin', body: 'Belongs to Tobin.' },
  ],
};
