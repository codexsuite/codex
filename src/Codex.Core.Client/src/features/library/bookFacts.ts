import type { LibraryBook } from '../../api/types';
import { bookScreens } from '../../shell/deepLink';
import { bookScreenLink } from '../../shell/links';
import { getLastScreen } from '../../shell/stores';

/** Where a tile opens: the screen last used in the book (this browser), else the server's hint, else Plan. */
export function openLink(book: Pick<LibraryBook, 'id' | 'lastScreen'>): string {
  const hinted = bookScreens.find((s) => s === book.lastScreen);
  return bookScreenLink(book.id, getLastScreen(book.id) ?? hinted ?? 'plan');
}

export function wordsLabel(words: number): string {
  return words >= 1000 ? `${(words / 1000).toFixed(1)}k words` : `${words} words`;
}

export function overline(seriesNumber: number | null): string | null {
  return seriesNumber === null ? null : `Book ${String(seriesNumber).padStart(3, '0')}`;
}
