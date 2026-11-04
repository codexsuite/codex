import type { SaveState } from '@codex/core/editing/saveStatusStore';

/** The words for the save status on a screen that has no book bar dot. */
export const saveStatusText: Record<SaveState, string> = {
  saved: 'Saved',
  saving: 'Saving…',
  unsaved: 'Unsaved',
  conflict: 'Changed elsewhere; not saved',
  remoteChanged: 'Changed elsewhere',
  retry: 'Not saved; will try again',
  offline: 'Offline; will save when you are back online',
};
