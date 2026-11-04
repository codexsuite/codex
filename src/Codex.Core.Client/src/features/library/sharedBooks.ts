import { bookRoles, parseBookRole, type BookRole } from '../../api/bookRole';
import type { Library, LibraryBook, LibrarySeries } from '../../api/types';

// Books shared with you (#80; design/README.md -> Shared with you). Pure: no React.

/** All / Mine / Shared with me, kept in the hash (`#filter=mine|shared`; All is the default and leaves it out). */
export const libraryFilters = ['all', 'mine', 'shared'] as const;
export type LibraryFilter = (typeof libraryFilters)[number];

export function readFilter(hash: string): LibraryFilter {
  const value = new URLSearchParams(hash.replace(/^#/, '')).get('filter');
  return libraryFilters.find((f) => f === value) ?? 'all';
}

/** The hash with `filter` set (or removed for All), other keys such as `series` kept. */
export function withFilter(hash: string, filter: LibraryFilter): string {
  const params = new URLSearchParams(hash.replace(/^#/, ''));
  if (filter === 'all') params.delete('filter');
  else params.set('filter', filter);
  const text = params.toString();
  return text === '' ? '' : `#${text}`;
}

export const isShared = (book: Pick<LibraryBook, 'role'>) => book.role !== 'owner';

/** The shelf under a filter. A series is someone else's as a whole, so it follows its books. */
export function filterLibrary(library: Library, filter: LibraryFilter): Library {
  if (filter === 'all') return library;
  const keep = (book: LibraryBook) => (filter === 'shared') === isShared(book);
  return {
    series: library.series.filter((s) => s.books.some(keep)),
    standalone: library.standalone.filter(keep),
  };
}

export interface SeriesShare {
  owner: string;
  /** Your access across the books you see in it: the lowest of their roles. */
  role: BookRole;
}

/** Who shared a series with you and your role on it; null for your own series. */
export function seriesShare(series: LibrarySeries): SeriesShare | null {
  const shared = series.books.filter(isShared);
  if (shared.length === 0) return null;
  const role = shared
    .map((b) => parseBookRole(b.role))
    .reduce((low, r) => (bookRoles.indexOf(r) < bookRoles.indexOf(low) ? r : low));
  return { owner: shared[0].owner.displayName, role };
}
