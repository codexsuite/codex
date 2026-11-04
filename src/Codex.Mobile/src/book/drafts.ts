import { platform } from '@codex/core/platform';

// Text an autosave gave up on (it could not be sent after the retries), kept in the phone's store so the field shows
// it again the next time it opens. `key` is `<area>:<id>` within the book. A draft remembers the server text the edit
// started from (`base`): if the server has something else by the time the field opens, the draft is stale and dropped,
// so it never silently overwrites newer text.
interface Draft {
  value: unknown;
  base: unknown;
}

const storageKey = (bookId: string, key: string) => `codex.draft.${bookId}.${key}`;

export function keepDraft(bookId: string, key: string, value: unknown, base: unknown): void {
  try {
    platform()
      .persistentStore()
      .setItem(storageKey(bookId, key), JSON.stringify({ value, base } satisfies Draft));
  } catch {
    // Storage blocked or full: the draft is lost, as it would be without this.
  }
}

export function forgetDraft(bookId: string, key: string): void {
  try {
    platform().persistentStore().removeItem(storageKey(bookId, key));
  } catch {
    // Nothing to forget.
  }
}

/** The kept text, only while the server still has the text the edit started from (`currentBase`); else forgotten. */
export function readDraft<T>(bookId: string, key: string, currentBase: T): T | undefined {
  try {
    const raw = platform().persistentStore().getItem(storageKey(bookId, key));
    if (raw === null) return undefined;
    const draft = JSON.parse(raw) as Draft;
    if (draft.base === currentBase) return draft.value as T;
    forgetDraft(bookId, key);
    return undefined;
  } catch {
    return undefined;
  }
}
