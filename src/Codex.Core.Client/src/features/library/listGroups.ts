import { statusOptions, type LibraryGroup, type LibrarySort } from '../../api/library';
import type { Library, LibraryBook } from '../../api/types';
import { seriesShare } from './sharedBooks';

export interface ListGroup {
  key: string;
  /** Null for the ungrouped list: no group row is drawn. */
  label: string | null;
  books: LibraryBook[];
  /** A series group someone else shared with you: who (#80). */
  sharedBy?: string;
}

const missingProgress = -1;

function flatCompare(sort: LibrarySort): (a: LibraryBook, b: LibraryBook) => number {
  switch (sort) {
    case 'title':
      return (a, b) => a.title.localeCompare(b.title);
    case 'series':
      return (a, b) =>
        (a.seriesName ?? '￿').localeCompare(b.seriesName ?? '￿') ||
        (a.seriesNumber ?? 0) - (b.seriesNumber ?? 0) ||
        a.title.localeCompare(b.title);
    case 'progress':
      return (a, b) =>
        (b.progressPct ?? missingProgress) - (a.progressPct ?? missingProgress) ||
        a.title.localeCompare(b.title);
    case 'lastEdited':
      // ISO timestamps compare correctly as strings.
      return (a, b) => b.lastEditedAt.localeCompare(a.lastEditedAt);
  }
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * The list view's groups. The server orders series and standalone books within `LibraryDto`; a grouping that
 * mixes them (status, none) flattens the books and re-applies the chosen sort to the merged list.
 */
export function listGroups(library: Library, group: LibraryGroup, sort: LibrarySort): ListGroup[] {
  if (group === 'series') {
    const groups: ListGroup[] = library.series.map((series) => ({
      key: series.id,
      label: series.name,
      books: series.books,
      sharedBy: seriesShare(series)?.owner,
    }));
    if (library.standalone.length > 0) {
      groups.push({ key: 'standalone', label: 'Standalone', books: library.standalone });
    }
    return groups;
  }
  const all = [...library.series.flatMap((series) => series.books), ...library.standalone].sort(
    flatCompare(sort),
  );
  if (group === 'none') return all.length === 0 ? [] : [{ key: 'all', label: null, books: all }];
  return statusOptions
    .map((status) => ({
      key: status,
      label: capitalise(status),
      books: all.filter((book) => book.status === status),
    }))
    .filter((entry) => entry.books.length > 0);
}
