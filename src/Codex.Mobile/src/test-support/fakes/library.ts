import type { Library, LibraryBook, LibrarySeries } from '@codex/core/api/types';

// Typed by the core's DTOs; override only what a spec cares about.
let counter = 0;
const next = () => ++counter;

export function libraryBook(overrides: Partial<LibraryBook> = {}): LibraryBook {
  const n = next();
  return {
    id: `book-${n}`,
    title: `Book ${n}`,
    subtitle: null,
    authorName: 'JD',
    seriesId: null,
    seriesName: null,
    seriesNumber: null,
    status: 'writing',
    coverPalette: 'cover-1',
    coverInitials: 'B',
    wordCount: 42_000,
    targetWords: 90_000,
    progressPct: 47,
    lastEditedAt: '2026-10-20T09:00:00Z',
    lastEditedLabel: '2h ago',
    lastScreen: null,
    archived: false,
    coverUrl: null,
    editedSincePublication: false,
    role: 'owner',
    owner: { id: 'user-jd', displayName: 'JD' },
    ...overrides,
  };
}

export function librarySeries(overrides: Partial<LibrarySeries> = {}): LibrarySeries {
  const n = next();
  const books = overrides.books ?? [libraryBook({ seriesNumber: 1 })];
  return {
    id: `series-${n}`,
    name: `Series ${n}`,
    description: null,
    bookCount: books.length,
    publishedCount: 0,
    wordCount: books.reduce((sum, book) => sum + book.wordCount, 0),
    lastEditedAt: '2026-10-20T09:00:00Z',
    lastEditedLabel: '2h ago',
    books,
    partlyShared: false,
    archived: false,
    ...overrides,
  };
}

export function library(overrides: Partial<Library> = {}): Library {
  return { series: [], standalone: [], ...overrides };
}
