import {
  createSaveStatusStore,
  saveStatusStore,
  type SaveState,
  type SaveStatusStore,
} from '@codex/core/editing/saveStatusStore';

// The book bar has one save dot, but many things autosave: every scene of the open chapter, a scene's title or summary,
// a Knowledge entry's summary, the style guide. Each autosave reports to its own store (a "source", keyed
// `'<area>:<id>'`), and the dot shows the worst state across the sources of the book that is open. An edit that cannot
// be sent outranks plain unsaved; a conflict is the worst. Sources are scoped to a book: an unsaved source of book A
// keeps its controller (so it keeps retrying) but does not colour the dot while book B is open.
const severity: Record<SaveState, number> = {
  saved: 0,
  saving: 1,
  unsaved: 2,
  offline: 3,
  retry: 4,
  remoteChanged: 5,
  conflict: 6,
};

export function worstState(states: Iterable<SaveState>): SaveState {
  let worst: SaveState = 'saved';
  for (const state of states) {
    if (severity[state] > severity[worst]) worst = state;
  }
  return worst;
}

/** The sources of one book. */
export interface SaveSources {
  /** The store one autosave's `AutosaveController` reports to. `key` is `<area>:<id>` within the book (one source per field or scene); the same key always gets the same store. */
  store(key: string): SaveStatusStore;
  /**
   * The autosave is finished (disposed): it no longer counts towards the dot. Only if `store` is still the one
   * registered under `key`, so a screen that has since taken the source over is not released by the one that left.
   */
  release(key: string, store: SaveStatusStore): void;
}

export interface BookSaveStatus {
  /** The sources of `bookId`. The first book that gets a source is the open one until `activate` says otherwise. */
  forBook(bookId: string): SaveSources;
  /** The book now open: its sources drive the dot. */
  activate(bookId: string): void;
  /** Forgets every source and clears the dot (tests). */
  reset(): void;
}

interface Source {
  store: SaveStatusStore;
  state: SaveState;
  waiting: number;
}

/** Per-source stores whose combined state, for the open book, drives `bar` (the one store behind the book bar's dot). */
export function createBookSaveStatus(bar: SaveStatusStore): BookSaveStatus {
  const books = new Map<string, Map<string, Source>>();
  let active: string | null = null;

  const publish = () => {
    const sources = active === null ? undefined : books.get(active);
    bar.set(worstState([...(sources?.values() ?? [])].map((s) => s.state)));
    bar.setWaiting([...(sources?.values() ?? [])].reduce((sum, s) => sum + s.waiting, 0));
  };

  const sourcesOf = (bookId: string) => {
    let sources = books.get(bookId);
    if (!sources) {
      sources = new Map();
      books.set(bookId, sources);
    }
    return sources;
  };

  return {
    forBook(bookId) {
      return {
        store(key) {
          const sources = sourcesOf(bookId);
          const existing = sources.get(key);
          if (existing) return existing.store;
          if (active === null) active = bookId;
          const inner = createSaveStatusStore();
          const source: Source = { store: undefined as never, state: 'saved', waiting: 0 };
          // A store that was released may still be written to by a controller on its way out: it counts for nothing.
          const report = () => {
            if (books.get(bookId)?.get(key) !== source) return;
            source.state = inner.getState();
            source.waiting = inner.getWaiting();
            if (active === bookId) publish();
          };
          source.store = {
            getState: inner.getState,
            subscribe: inner.subscribe,
            getWaiting: inner.getWaiting,
            setWaiting(count) {
              inner.setWaiting(count);
              report();
            },
            set(state) {
              inner.set(state);
              report();
            },
            setRemoteChange() {
              inner.setRemoteChange();
              report();
            },
          };
          sources.set(key, source);
          return source.store;
        },
        release(key, store) {
          const sources = books.get(bookId);
          if (sources?.get(key)?.store !== store) return;
          sources.delete(key);
          if (sources.size === 0) books.delete(bookId);
          if (active === bookId) publish();
        },
      };
    },
    activate(bookId) {
      if (active === bookId) return;
      active = bookId;
      publish();
    },
    reset() {
      books.clear();
      active = null;
      publish();
    },
  };
}

/** The app's one registry, behind the core's `saveStatusStore` that the book bar reads. */
export const bookSaveStatus = createBookSaveStatus(saveStatusStore);
