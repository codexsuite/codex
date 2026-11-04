import { useEffect, useState } from 'react';
import {
  AutosaveController,
  type AutosaveOptions,
  type Scheduler,
} from '@codex/core/editing/autosaveController';
import {
  saveStatusStore,
  type SaveState,
  type SaveStatusStore,
} from '@codex/core/editing/saveStatusStore';
import { useSaveStatus } from './useSaveStatus';

export interface UseAutosaveOptions<T> {
  // The current value of whatever is being edited (prose, a summary). Compared by identity.
  value: T;
  // The server version the value is based on. A change from outside (after a reload) re-bases the hook.
  version: number;
  // The lane's endpoint call. A 409 ApiRequestError with code 'conflict' means the version was stale.
  save: (value: T, version: number) => Promise<{ version: number }>;
  onConflict?: () => void;
  // A 403 `forbidden` (#80): the edit is dropped (toast and the header re-read); put the server's text back here.
  onForbidden?: () => void;
  delayMs?: number;
  scheduler?: Scheduler;
  isOffline?: () => boolean;
  store?: SaveStatusStore;
}

export interface UseAutosave {
  status: SaveState;
  // Call from onBlur: saves straight away.
  flush: () => void;
}

export function useAutosave<T>(options: UseAutosaveOptions<T>): UseAutosave {
  const store = options.store ?? saveStatusStore;
  const full: AutosaveOptions<T> = { ...options, store };
  const [controller] = useState(() => new AutosaveController<T>(full));
  controller.configure(full);

  useEffect(() => {
    controller.sync(options.value, options.version);
  }, [controller, options.value, options.version]);
  useEffect(() => () => controller.dispose(), [controller]);

  return { status: useSaveStatus(store), flush: () => void controller.flush() };
}
