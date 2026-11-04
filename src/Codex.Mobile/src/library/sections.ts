import type { Library, LibraryBook } from '@codex/core/api/types';

// What the Library and the book switcher show, from the Library DTO: search, series groups, the recent list and the
// short labels on a row. Pure, so vitest covers it.

export interface BookSection {
  key: string;
  title: string;
  /** `· 3 books` after the title; 0 for no count. */
  bookCount: number;
  data: LibraryBook[];
}

const STANDALONE_KEY = 'standalone';

/** Series in the server's order, then Standalone. Empty groups are skipped. */
export function librarySections(library: Library): BookSection[] {
  const sections: BookSection[] = library.series
    .filter((series) => series.books.length > 0)
    .map((series) => ({
      key: series.id,
      title: series.name,
      bookCount: series.books.length,
      data: series.books,
    }));
  if (library.standalone.length > 0) {
    sections.push({
      key: STANDALONE_KEY,
      title: 'Standalone',
      bookCount: library.standalone.length,
      data: library.standalone,
    });
  }
  return sections;
}

const includes = (text: string | null, needle: string) =>
  text !== null && text.toLowerCase().includes(needle);

/**
 * Keeps the books whose title, subtitle or series name contain `query` (case and surrounding spaces ignored). A series
 * whose own name matches keeps all its books. A blank query returns the library as it is.
 */
export function filterLibrary(library: Library, query: string): Library {
  const needle = query.trim().toLowerCase();
  if (needle === '') return library;
  const matches = (book: LibraryBook) =>
    includes(book.title, needle) ||
    includes(book.subtitle, needle) ||
    includes(book.seriesName, needle);
  return {
    series: library.series
      .map((series) => ({
        ...series,
        books: includes(series.name, needle) ? series.books : series.books.filter(matches),
      }))
      .filter((series) => series.books.length > 0),
    standalone: library.standalone.filter(matches),
  };
}

export function allBooks(library: Library): LibraryBook[] {
  return [...library.series.flatMap((series) => series.books), ...library.standalone];
}

export function findBook(library: Library, bookId: string): LibraryBook | null {
  return allBooks(library).find((book) => book.id === bookId) ?? null;
}

/** The most recently edited books first, without `exceptBookId` (the book that is open). */
export function recentBooks(library: Library, exceptBookId: string, limit: number): LibraryBook[] {
  return allBooks(library)
    .filter((book) => book.id !== exceptBookId)
    .sort((a, b) =>
      a.lastEditedAt < b.lastEditedAt ? 1 : a.lastEditedAt > b.lastEditedAt ? -1 : 0,
    )
    .slice(0, limit);
}

/** `Book 003` for a numbered book in a series, null otherwise. */
export function bookOverline(book: LibraryBook): string | null {
  return book.seriesId === null || book.seriesNumber === null
    ? null
    : `Book ${String(book.seriesNumber).padStart(3, '0')}`;
}

/** `98.4k`, `12k`, `850`. */
export function shortCount(value: number): string {
  if (value < 1000) return String(value);
  const thousands = Math.round(value / 100) / 10;
  return `${Number.isInteger(thousands) ? thousands : thousands.toFixed(1)}k`;
}

/** `98.4k / 120k words`, or `850 words` when the book has no target. */
export function wordsLabel(book: Pick<LibraryBook, 'wordCount' | 'targetWords'>): string {
  return book.targetWords === null
    ? `${shortCount(book.wordCount)} words`
    : `${shortCount(book.wordCount)} / ${shortCount(book.targetWords)} words`;
}

/** `98.4k / 120k words · 2h ago`; the edited part is the server's label. */
export function bookMeta(book: LibraryBook): string {
  return `${wordsLabel(book)} · ${book.lastEditedLabel}`;
}

/** `98.4k words · 2h ago`: the switcher's row meta. */
export function switcherMeta(book: Pick<LibraryBook, 'wordCount' | 'lastEditedLabel'>): string {
  return `${shortCount(book.wordCount)} words · ${book.lastEditedLabel}`;
}

/** 0 to 100 for the progress bar; a book without a figure shows an empty bar. */
export function progressWidth(progressPct: number | null): number {
  if (progressPct === null || Number.isNaN(progressPct)) return 0;
  return Math.min(100, Math.max(0, progressPct));
}

/** `needs_check` -> `Needs check`. The status travels as the stored value. */
export function statusLabel(status: string): string {
  const words = status.replace(/[_-]+/g, ' ').trim();
  return words === '' ? '' : words[0].toUpperCase() + words.slice(1);
}

/** `write` -> `Write`: where the Continue card says the book was left. */
export function tabLabel(tab: string): string {
  return tab === '' ? '' : tab[0].toUpperCase() + tab.slice(1);
}
