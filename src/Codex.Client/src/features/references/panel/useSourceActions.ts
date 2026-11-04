import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { sourceKey, type SourceDetail } from '@codex/core/api/referenceSources';
import { refreshClearance } from '../refreshClearance';

export interface SourceActions {
  /**
   * Runs a write that answers with the refreshed panel; on failure nothing changes and `error` says why. A quiet
   * write (a field saved on blur) leaves the buttons enabled, so the click that caused the blur still lands.
   */
  run: (write: () => Promise<SourceDetail>, quiet?: boolean) => Promise<boolean>;
  /** Refreshes everything a clearance change touches: the list, the strip, the book bar badge and the markers. */
  refresh: () => Promise<void>;
  busy: boolean;
  error: string | null;
  fail: (message: string) => void;
}

// One panel's writes. Every write refreshes what clearance touches (refreshClearance).
export function useSourceActions(bookId: string, sourceId: string): SourceActions {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = async () => {
    await refreshClearance(queryClient, bookId);
  };

  const run = async (write: () => Promise<SourceDetail>, quiet = false) => {
    if (!quiet) setBusy(true);
    setError(null);
    try {
      queryClient.setQueryData(sourceKey(sourceId), await write());
      await refresh();
      return true;
    } catch (caught) {
      setError(
        caught instanceof ApiRequestError ? caught.message : 'The change could not be saved.',
      );
      return false;
    } finally {
      if (!quiet) setBusy(false);
    }
  };

  return { run, refresh, busy, error, fail: setError };
}
