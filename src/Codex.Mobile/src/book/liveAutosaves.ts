import type { SaveState, SaveStatusStore } from '@codex/core/editing/saveStatusStore';

// An autosave that has something to send outlives the screen it was typed on (leaving must not lose an edit), and the
// screen can come back before it is done. So the live controllers are kept here, one per `area:id` within a book:
// opening the same field or scene again adopts the controller that is still alive (and its pending text) instead of
// starting a second one that would race it, and a controller nobody is using retires itself.

/** Seconds are for people: after leaving, a failed save is tried again after each of these waits, then given up. */
export const RETRY_BACKOFF_MS = [5_000, 15_000, 45_000] as const;

export interface LiveAutosave {
  /** The `AutosaveController`; the registry only needs these two. */
  controller: { flush(): Promise<void>; dispose(): void };
  store: SaveStatusStore;
  /** The newest value and version the field reported (what a screen coming back shows as pending). */
  latest: { value: unknown; version: number };
  /** The server text the pending edit started from: the first value, then each value saved (what a draft is based on). */
  base?: unknown;
  /** Whatever else the owner keeps with the controller (Write's per-scene save bookkeeping). */
  data?: unknown;
  /** Bumped each time a screen takes the controller over; a leave that sees a newer number stops. */
  claims: number;
  cancelWait?: () => void;
}

const books = new Map<string, Map<string, LiveAutosave>>();

export const liveAutosaves = {
  get(bookId: string, key: string): LiveAutosave | undefined {
    return books.get(bookId)?.get(key);
  },
  add(bookId: string, key: string, live: LiveAutosave): void {
    let sources = books.get(bookId);
    if (!sources) {
      sources = new Map();
      books.set(bookId, sources);
    }
    sources.set(key, live);
  },
  /** Forgets `live` (only if it is still the one registered under the key). */
  remove(bookId: string, key: string, live: LiveAutosave): void {
    const sources = books.get(bookId);
    if (sources?.get(key) !== live) return;
    sources.delete(key);
    if (sources.size === 0) books.delete(bookId);
  },
  /** A screen takes the controller over: whatever was waiting to retire it stops. */
  claim(live: LiveAutosave): void {
    live.claims += 1;
    live.cancelWait?.();
    live.cancelWait = undefined;
  },
  /** Forgets every controller and stops what they wait for (tests). */
  reset(): void {
    for (const sources of books.values()) {
      for (const live of sources.values()) {
        live.cancelWait?.();
        live.controller.dispose();
      }
    }
    books.clear();
  },
};

/** Resolves with the state once the store is neither saving nor waiting for the connection (or `stop()` says so). */
function settled(store: SaveStatusStore, stop: () => boolean): Promise<SaveState> {
  return new Promise((resolve) => {
    const check = () => {
      const state = store.getState();
      if (state === 'saving' || state === 'offline') {
        if (!stop()) return;
      }
      unsubscribe();
      resolve(state);
    };
    const unsubscribe = store.subscribe(check);
    check();
  });
}

export interface Leaving {
  /** Sends what is waiting (flush) and waits for a request already in flight. */
  settle(): Promise<unknown>;
  /**
   * Done with the controller: dispose it, release its save-status source and forget it. `unsaved` is true when text
   * the server does not have is being given up, so the owner keeps it as a draft.
   */
  retire(unsaved: boolean): void;
}

/**
 * The screen is gone. The controller stays only while it can still do something: a request in flight, an edit
 * waiting for the connection, or a refused one it tries again (after 5, 15 and 45 s). A saved one retires at once;
 * one that is still not saved after the last try retires with its text kept as a draft, so the book bar's dot never
 * stays coloured with nothing able to clear it. Taking the controller over again (`liveAutosaves.claim`) stops this.
 */
export function leave(live: LiveAutosave, { settle, retire }: Leaving): void {
  const claim = live.claims;
  const mine = () => live.claims === claim;
  void (async () => {
    await settle();
    for (let attempt = 0; ; attempt++) {
      const state = await settled(live.store, () => !mine());
      if (!mine()) return;
      if (state === 'saved') return retire(false);
      if ((state !== 'retry' && state !== 'unsaved') || attempt >= RETRY_BACKOFF_MS.length) {
        return retire(true);
      }
      await new Promise<void>((resolve) => {
        const timer = setTimeout(resolve, RETRY_BACKOFF_MS[attempt]);
        live.cancelWait = () => {
          clearTimeout(timer);
          resolve();
        };
      });
      live.cancelWait = undefined;
      if (!mine()) return;
      await live.controller.flush();
    }
  })();
}
