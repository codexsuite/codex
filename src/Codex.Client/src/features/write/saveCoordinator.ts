import { createContext, useContext } from 'react';
import {
  createSaveStatusStore,
  type SaveState,
  type SaveStatusStore,
} from '@codex/core/editing/saveStatusStore';

// Every editor on the Write screen (a scene's prose, a scene's summary) autosaves on its own and reports to its own
// store. The book bar has one save-status slot, so the coordinator shows the worst state across them:
// Unsaved > Saving… > Saved; an edit that cannot be sent (offline, refused) outranks plain Unsaved. A stale block
// operation (409) is the worst of all: the bar shows "Conflict · Reload".
const severity: Record<SaveState, number> = {
  saved: 0,
  saving: 1,
  unsaved: 2,
  offline: 3,
  retry: 4,
  remoteChanged: 5,
  conflict: 6,
};

// States that hold an edit the server does not have yet.
const waitingStates: ReadonlySet<SaveState> = new Set(['unsaved', 'retry', 'offline']);

export function worstState(states: Iterable<SaveState>): SaveState {
  let worst: SaveState = 'saved';
  for (const state of states) {
    if (severity[state] > severity[worst]) worst = state;
  }
  return worst;
}

export const summaryKey = (sceneId: string) => `${sceneId}:summary`;

// The book's "Updated elsewhere": the realtime client (src/realtime/remoteChanges.ts) sets it on the bar's store and
// cannot say which scene; the coordinator keeps it as a state of its own until Reload, so another editor's next
// state change does not clear it.
const remoteKey = '@remote';

export class SaveCoordinator {
  private readonly states = new Map<string, SaveState>();
  private readonly stores = new Map<string, SaveStatusStore>();
  private attached = true;
  private published: SaveState;
  private readonly unwatch: () => void;

  constructor(private readonly bar: SaveStatusStore) {
    this.published = bar.getState();
    this.unwatch = bar.subscribe(() => this.watchBar());
  }

  /** The store one editor passes to `useAutosave`. The same key always gets the same store. */
  store(key: string): SaveStatusStore {
    const existing = this.stores.get(key);
    if (existing) return existing;
    const inner = createSaveStatusStore();
    const wrapped: SaveStatusStore = {
      getState: inner.getState,
      subscribe: inner.subscribe,
      getWaiting: inner.getWaiting,
      setWaiting: inner.setWaiting,
      set: (state) => {
        inner.set(state);
        this.report(key, inner.getState());
      },
      setRemoteChange: () => {
        inner.setRemoteChange();
        this.report(key, inner.getState());
      },
    };
    this.stores.set(key, wrapped);
    return wrapped;
  }

  /** An editor left the screen. */
  release(key: string) {
    this.stores.delete(key);
    this.states.delete(key);
    this.publish();
  }

  /** Keys (scene ids for prose) whose last save was refused as stale. */
  conflicts(): string[] {
    return [...this.states].filter(([, state]) => state === 'conflict').map(([key]) => key);
  }

  /** Keys (scene ids for prose) whose last save was refused by the server: the bar's Retry saves these again. */
  refused(): string[] {
    return [...this.states].filter(([, state]) => state === 'retry').map(([key]) => key);
  }

  /** Whether any edit is still waiting for the server (unsaved, refused or offline). */
  hasUnsaved(): boolean {
    return [...this.states.values()].some(
      (state) => state === 'saving' || waitingStates.has(state),
    );
  }

  /** Whether an edit is stuck (offline or refused): leaving the page would lose it. */
  hasStuck(): boolean {
    return [...this.states.values()].some((state) => state === 'retry' || state === 'offline');
  }

  /** Whether the bar says "Updated elsewhere": another session changed a scene on screen. */
  remoteChanged(): boolean {
    return [...this.states.values()].includes('remoteChanged');
  }

  /** After Reload has read the stale scenes again: "Updated elsewhere" goes. */
  clearRemoteChange() {
    this.states.delete(remoteKey);
    for (const [key, state] of this.states) {
      if (state === 'remoteChanged') this.stores.get(key)?.set('saved');
    }
    this.publish();
  }

  /** The screen is going away: the bar goes back to Saved and stops following the editors. */
  detach() {
    this.attached = false;
    this.unwatch();
    this.bar.set('saved');
  }

  private report(key: string, state: SaveState) {
    this.states.set(key, state);
    this.publish();
  }

  // Anything but this coordinator turning the bar to "Updated elsewhere" is the realtime client's remote change.
  private watchBar() {
    if (!this.attached) return;
    if (this.bar.getState() === 'remoteChanged' && this.published !== 'remoteChanged') {
      this.states.set(remoteKey, 'remoteChanged');
      this.publish();
    }
  }

  private publish() {
    if (!this.attached) return;
    this.published = worstState(this.states.values());
    this.bar.setWaiting(
      [...this.states.values()].filter((state) => waitingStates.has(state)).length,
    );
    this.bar.set(this.published);
  }
}

export const SaveCoordinatorContext = createContext<SaveCoordinator | null>(null);

export function useSaveCoordinator(): SaveCoordinator {
  const coordinator = useContext(SaveCoordinatorContext);
  if (coordinator === null) throw new Error('Write editors need a <SaveCoordinatorContext>.');
  return coordinator;
}
