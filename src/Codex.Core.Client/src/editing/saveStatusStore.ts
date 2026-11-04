// unsaved: an edit is waiting for its save. saving: a request is in flight. saved: nothing is waiting.
// conflict: the server has a newer version than the one this edit was based on (a block operation; the editor's own
// save wins instead, last write wins). remoteChanged: another session changed the same thing (set by the realtime
// lane). retry: the server refused the save (an error); the edit is kept and a Retry button saves it again.
// offline: the browser has no connection; the edit is kept and saves by itself when it is back.
export type SaveState =
  'unsaved' | 'saving' | 'saved' | 'conflict' | 'remoteChanged' | 'retry' | 'offline';

export interface SaveStatusStore {
  getState(): SaveState;
  subscribe(listener: () => void): () => void;
  set(state: SaveState): void;
  setRemoteChange(): void;
  /** How many edits are waiting (shown by "Offline · 2 changes waiting"); the coordinator keeps it. */
  getWaiting(): number;
  setWaiting(count: number): void;
}

export function createSaveStatusStore(initial: SaveState = 'saved'): SaveStatusStore {
  let state = initial;
  let waiting = 0;
  const listeners = new Set<() => void>();
  const set = (next: SaveState) => {
    if (next === state) return;
    state = next;
    listeners.forEach((listener) => listener());
  };
  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    set,
    // A conflict is already the stronger message; leave it.
    setRemoteChange: () => {
      if (state !== 'conflict') set('remoteChanged');
    },
    getWaiting: () => waiting,
    setWaiting: (count) => {
      if (count === waiting) return;
      waiting = count;
      listeners.forEach((listener) => listener());
    },
  };
}

// The one store behind the book bar's save-status slot.
export const saveStatusStore = createSaveStatusStore();
