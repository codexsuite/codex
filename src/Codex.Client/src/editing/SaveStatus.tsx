import { useSyncExternalStore } from 'react';
import { editingTestIds } from './testIds';
import {
  saveStatusStore,
  type SaveState,
  type SaveStatusStore,
} from '@codex/core/editing/saveStatusStore';
import { useSaveStatus } from './useSaveStatus';

function useWaiting(store: SaveStatusStore): number {
  return useSyncExternalStore(store.subscribe, store.getWaiting);
}

const labels: Record<SaveState, string> = {
  unsaved: 'Unsaved',
  saving: 'Saving…',
  saved: 'Saved',
  conflict: 'Conflict',
  remoteChanged: 'Updated elsewhere',
  retry: 'Unsaved',
  offline: 'Offline',
};

const dots: Record<SaveState, string> = {
  unsaved: 'bg-warn',
  saving: 'bg-accent animate-pulse',
  saved: 'bg-ok',
  conflict: 'bg-danger',
  remoteChanged: 'bg-warn',
  retry: 'bg-danger',
  offline: 'bg-warn',
};

interface SaveStatusProps {
  store?: SaveStatusStore;
  // Called by the Reload button shown for "Updated elsewhere" and, when given, for "Conflict" (a stale save); the lane
  // refetches and marks the store saved.
  onReload?: () => void;
  // Called by the Retry button shown for an edit the server refused (state "retry"); the lane saves it again.
  onRetry?: () => void;
}

// Fills the shell's save-status slot: a 6px dot in the state's tone and the state in muted text.
export function SaveStatus({ store = saveStatusStore, onReload, onRetry }: SaveStatusProps) {
  const state = useSaveStatus(store);
  const waiting = useWaiting(store);
  return (
    <span
      data-testid={editingTestIds.saveStatus}
      // A refused save is still an unsaved edit to anything reading the machine value; data-retry tells them apart.
      data-state={state === 'retry' ? 'unsaved' : state}
      data-retry={state === 'retry' ? 'true' : undefined}
      role="status"
      aria-live="polite"
      className="inline-flex items-center gap-[5px] text-xs whitespace-nowrap text-muted"
    >
      <i aria-hidden="true" className={`size-1.5 flex-none rounded-full ${dots[state]}`} />
      {state === 'remoteChanged' || (state === 'conflict' && onReload) ? (
        <>
          {labels[state]} ·{' '}
          <button
            type="button"
            data-testid={editingTestIds.saveStatusReload}
            className="underline"
            onClick={onReload}
          >
            Reload
          </button>
        </>
      ) : state === 'retry' && onRetry ? (
        <>
          {labels[state]} ·{' '}
          <button
            type="button"
            data-testid={editingTestIds.saveStatusRetry}
            className="underline"
            onClick={onRetry}
          >
            Retry
          </button>
        </>
      ) : state === 'offline' ? (
        `Offline · ${waiting} ${waiting === 1 ? 'change' : 'changes'} waiting`
      ) : (
        labels[state]
      )}
    </span>
  );
}
