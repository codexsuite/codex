import { useSyncExternalStore } from 'react';
import {
  saveStatusStore,
  type SaveState,
  type SaveStatusStore,
} from '@codex/core/editing/saveStatusStore';

export function useSaveStatus(store: SaveStatusStore = saveStatusStore): SaveState {
  return useSyncExternalStore(store.subscribe, store.getState);
}
