import { platform, type KeyValueStore } from '../platform';
import { bookScreens, type BookScreen } from './deepLink';

// Two small per-book stores. Storage can be missing or throw (private windows), so every access is guarded and
// the app behaves as if nothing was stored.

const listeners = new Set<() => void>();
const notify = () => listeners.forEach((l) => l());
export const subscribeStores = (listener: () => void) => {
  listeners.add(listener);
  return () => void listeners.delete(listener);
};

function read(store: () => KeyValueStore, key: string): string | null {
  try {
    return store().getItem(key);
  } catch {
    return null;
  }
}

function write(store: () => KeyValueStore, key: string, value: string | null) {
  try {
    if (value === null) store().removeItem(key);
    else store().setItem(key, value);
  } catch {
    // nothing stored; the next read says so
  }
  notify();
}

const local = () => platform().persistentStore();
const session = () => platform().sessionStore();

const lastKey = (bookId: string) => `codex.last.${bookId}`;
const highlightKey = (bookId: string) => `codex.hl.${bookId}`;

/** The screen last visited in a book (persistent `codex.last.<book>`), or null. */
export function getLastScreen(bookId: string): BookScreen | null {
  const stored = read(local, lastKey(bookId));
  return bookScreens.find((s) => s === stored) ?? null;
}

export function setLastScreen(bookId: string, screen: BookScreen) {
  write(local, lastKey(bookId), screen);
}

/** The Knowledge base entry highlighted in a book (session `codex.hl.<book>`), or null. */
export function getHighlight(bookId: string): string | null {
  return read(session, highlightKey(bookId));
}

export function setHighlight(bookId: string, entryId: string | null) {
  write(session, highlightKey(bookId), entryId);
}
