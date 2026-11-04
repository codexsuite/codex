import type { Library, LibraryBook, LibrarySeries } from '@codex/core/api/types';
import { describe, expect, it } from 'vitest';
import {
  allBooks,
  bookMeta,
  bookOverline,
  filterLibrary,
  findBook,
  librarySections,
  progressWidth,
  recentBooks,
  shortCount,
  statusLabel,
  switcherMeta,
  tabLabel,
  wordsLabel,
} from './sections';

const book = (id: string, extra: Partial<LibraryBook> = {}) =>
  ({
    id,
    title: `Book ${id}`,
    subtitle: null,
    seriesId: null,
    seriesName: null,
    seriesNumber: null,
    lastEditedAt: '2026-10-01T00:00:00Z',
    lastEditedLabel: '1d ago',
    wordCount: 0,
    targetWords: null,
    ...extra,
  }) as LibraryBook;
const series = (id: string, books: LibraryBook[]) =>
  ({ id, name: `Series ${id}`, books }) as LibrarySeries;

const shelf: Library = {
  series: [series('s1', [book('a'), book('b')]), series('s2', [book('c')])],
  standalone: [book('d')],
};

describe('librarySections', () => {
  it('lists series in order with their book counts, then standalone', () => {
    expect(
      librarySections(shelf).map((s) => [s.title, s.bookCount, s.data.map((b) => b.id)]),
    ).toEqual([
      ['Series s1', 2, ['a', 'b']],
      ['Series s2', 1, ['c']],
      ['Standalone', 1, ['d']],
    ]);
  });

  it('skips empty series and an empty standalone group', () => {
    expect(librarySections({ series: [series('s1', [])], standalone: [] })).toEqual([]);
  });
});

describe('filterLibrary', () => {
  it('returns the library untouched for a blank query', () => {
    expect(filterLibrary(shelf, '   ')).toBe(shelf);
  });

  it('matches titles case-insensitively and drops series left empty', () => {
    const found = filterLibrary(shelf, ' BOOK c ');
    expect(librarySections(found).map((s) => s.data.map((b) => b.id))).toEqual([['c']]);
  });

  it('keeps every book of a series whose own name matches', () => {
    const found = filterLibrary(shelf, 'series s1');
    expect(librarySections(found).map((s) => s.data.map((b) => b.id))).toEqual([['a', 'b']]);
  });

  it('matches a book by its series name or subtitle', () => {
    const library: Library = {
      series: [],
      standalone: [book('x', { seriesName: 'Gods', subtitle: 'A Tale' }), book('y')],
    };
    expect(filterLibrary(library, 'gods').standalone.map((b) => b.id)).toEqual(['x']);
    expect(filterLibrary(library, 'tale').standalone.map((b) => b.id)).toEqual(['x']);
  });

  it('finds nothing when nothing matches', () => {
    expect(librarySections(filterLibrary(shelf, 'zzz'))).toEqual([]);
  });
});

describe('allBooks, findBook and recentBooks', () => {
  it('lists series books first, then standalone', () => {
    expect(allBooks(shelf).map((b) => b.id)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('finds a book or null', () => {
    expect(findBook(shelf, 'c')?.id).toBe('c');
    expect(findBook(shelf, 'nope')).toBeNull();
  });

  it('orders by last edit, newest first, without the open book, up to the limit', () => {
    const library: Library = {
      series: [],
      standalone: [
        book('old', { lastEditedAt: '2026-10-01T00:00:00Z' }),
        book('new', { lastEditedAt: '2026-10-22T00:00:00Z' }),
        book('open', { lastEditedAt: '2026-10-23T00:00:00Z' }),
        book('mid', { lastEditedAt: '2026-10-10T00:00:00Z' }),
      ],
    };
    expect(recentBooks(library, 'open', 2).map((b) => b.id)).toEqual(['new', 'mid']);
  });
});

describe('row labels', () => {
  it('pads a series number to three digits and gives a standalone book no overline', () => {
    expect(bookOverline(book('a', { seriesId: 's', seriesNumber: 3 }))).toBe('Book 003');
    expect(bookOverline(book('a', { seriesId: 's', seriesNumber: null }))).toBeNull();
    expect(bookOverline(book('a'))).toBeNull();
  });

  it.each([
    [0, '0'],
    [850, '850'],
    [1000, '1k'],
    [98_400, '98.4k'],
    [120_000, '120k'],
    [99_960, '100k'],
  ])('shortCount(%i) -> %s', (value, expected) => {
    expect(shortCount(value)).toBe(expected);
  });

  it('shows words over target, or just the words without a target', () => {
    expect(wordsLabel({ wordCount: 98_400, targetWords: 120_000 })).toBe('98.4k / 120k words');
    expect(wordsLabel({ wordCount: 850, targetWords: null })).toBe('850 words');
  });

  it('adds the server edited label to the meta line', () => {
    expect(bookMeta(book('a', { wordCount: 850, lastEditedLabel: '2h ago' }))).toBe(
      '850 words · 2h ago',
    );
  });

  it('shows words and the edited label in the switcher', () => {
    expect(switcherMeta({ wordCount: 98_400, lastEditedLabel: 'Yesterday' })).toBe(
      '98.4k words · Yesterday',
    );
  });

  it.each([
    [null, 0],
    [47, 47],
    [-3, 0],
    [140, 100],
    [Number.NaN, 0],
  ])('progressWidth(%s) -> %s', (pct, expected) => {
    expect(progressWidth(pct)).toBe(expected);
  });

  it.each([
    ['drafting', 'Drafting'],
    ['needs_check', 'Needs check'],
    ['', ''],
  ])('statusLabel %j -> %j', (status, expected) => {
    expect(statusLabel(status)).toBe(expected);
  });

  it('capitalises the tab the Continue card names', () => {
    expect(tabLabel('write')).toBe('Write');
    expect(tabLabel('')).toBe('');
  });
});
