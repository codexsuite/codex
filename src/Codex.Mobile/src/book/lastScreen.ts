import { platform, type KeyValueStore } from '@codex/core/platform';
import type { Href } from 'expo-router';
import { bookTabs, entryLink, styleGuideLink, tabLink, type BookTab } from '../links';

// Last-screen memory (#35): which book was opened last and where in it (`codex.last.<id>` in design/README.md).
// The book layout records, the Library's Continue card reads. Stored in the platform's persistent store.

/** Where in a book: a tab, and the screen pushed on it (an entry id for Knowledge, `style-guide` for Settings). */
export interface LastScreen {
  tab: BookTab;
  pushed: string | null;
}

const LAST_BOOK_KEY = 'codex.last-book';
const screenKey = (bookId: string) => `codex.last.${bookId}`;

export function formatLastScreen(screen: LastScreen): string {
  return screen.pushed === null ? screen.tab : `${screen.tab}/${screen.pushed}`;
}

/** Null for anything that is not `<tab>` or `<tab>/<pushed>` with a known tab. */
export function parseLastScreen(value: string | null): LastScreen | null {
  if (value === null) return null;
  const slash = value.indexOf('/');
  const tab = slash < 0 ? value : value.slice(0, slash);
  const pushed = slash < 0 ? null : value.slice(slash + 1);
  const known = bookTabs.find((candidate) => candidate === tab);
  return known === undefined || pushed === '' ? null : { tab: known, pushed };
}

/**
 * The screen a book path shows: `/book/b1/knowledge/e1` is Knowledge with `e1` pushed. Null for a path that is not one
 * of the book's tab screens (the scene sheet is transient and not remembered).
 */
export function lastScreenOfPath(pathname: string): { bookId: string; screen: LastScreen } | null {
  const [root, bookId, tab, ...rest] = pathname.split('/').filter((part) => part !== '');
  const known = bookTabs.find((candidate) => candidate === tab);
  if (root !== 'book' || bookId === undefined || known === undefined) return null;
  return { bookId, screen: { tab: known, pushed: rest.length === 0 ? null : rest.join('/') } };
}

/** The link that opens `bookId` where `screen` says it was left (the Continue card). Unknown pushed screens open the tab. */
export function lastScreenLink(bookId: string, screen: LastScreen): Href {
  if (screen.tab === 'knowledge' && screen.pushed !== null) return entryLink(bookId, screen.pushed);
  if (screen.tab === 'settings' && screen.pushed === 'style-guide') return styleGuideLink(bookId);
  return tabLink(bookId, screen.tab);
}

export interface LastScreenMemory {
  /** Remembers `bookId` as the book last opened, on `screen`. */
  remember(bookId: string, screen: LastScreen): void;
  /** The book last opened, or null. */
  lastBookId(): string | null;
  /** Where that book was last left, or null (never opened here). */
  screenOf(bookId: string): LastScreen | null;
}

/** Memory over `store`. Storage may throw (blocked or full): remembering then does nothing and reading finds nothing. */
export function createLastScreenMemory(store: () => KeyValueStore): LastScreenMemory {
  const guard = <T>(action: (kv: KeyValueStore) => T, fallback: T): T => {
    try {
      return action(store());
    } catch {
      return fallback;
    }
  };
  return {
    remember(bookId, screen) {
      guard((kv) => {
        kv.setItem(screenKey(bookId), formatLastScreen(screen));
        kv.setItem(LAST_BOOK_KEY, bookId);
      }, undefined);
    },
    lastBookId: () => guard((kv) => kv.getItem(LAST_BOOK_KEY), null),
    screenOf: (bookId) => guard((kv) => parseLastScreen(kv.getItem(screenKey(bookId))), null),
  };
}

/** The app's memory, over the platform's persistent store (the phone's key-value store). */
export const lastScreenMemory: LastScreenMemory = createLastScreenMemory(() =>
  platform().persistentStore(),
);
