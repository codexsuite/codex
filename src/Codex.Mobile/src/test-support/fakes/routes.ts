import type { FakeHandler } from '../fakeApi';
import { bookHeader } from './book';
import { kb, kbEntry } from './kb';
import { library, libraryBook, librarySeries } from './library';
import { manuscript } from './manuscript';
import { outline } from './outline';
import { bookSettings } from './settings';

export const FAKE_BOOK_ID = 'book-1';

/**
 * A coherent small world for `installFakeApi`: a Library with one series book (`book-1`, "The Empty Seat") and one
 * standalone book, and `book-1`'s header, outline, Knowledge base, one entry, settings and manuscript. Spread it and
 * override the routes a spec cares about: `installFakeApi({ ...worldRoutes(), 'GET /api/books/book-1/kb': … })`.
 */
export function worldRoutes(): Record<string, FakeHandler> {
  const header = bookHeader({ id: FAKE_BOOK_ID });
  return {
    'GET /api/library': library({
      series: [
        librarySeries({
          id: 'series-1',
          name: 'Forsaken Gods',
          books: [
            libraryBook({
              id: FAKE_BOOK_ID,
              title: header.title,
              seriesId: 'series-1',
              seriesName: 'Forsaken Gods',
              seriesNumber: 3,
              coverPalette: header.coverPalette,
            }),
          ],
        }),
      ],
      standalone: [libraryBook({ id: 'book-2', title: 'Northbound' })],
    }),
    [`GET /api/books/${FAKE_BOOK_ID}`]: header,
    [`GET /api/books/${FAKE_BOOK_ID}/outline`]: outline({ bookId: FAKE_BOOK_ID }),
    [`GET /api/books/${FAKE_BOOK_ID}/kb`]: kb(),
    [`GET /api/books/${FAKE_BOOK_ID}/manuscript`]: manuscript({ bookId: FAKE_BOOK_ID }),
    [`GET /api/books/${FAKE_BOOK_ID}/settings`]: bookSettings({ id: FAKE_BOOK_ID }),
    'GET /api/kb/entries/entry-1': kbEntry({ bookId: FAKE_BOOK_ID }),
  };
}
