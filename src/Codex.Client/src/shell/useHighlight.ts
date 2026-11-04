import { useCallback, useSyncExternalStore } from 'react';
import { getHighlight, setHighlight, subscribeStores } from '@codex/core/shell/stores';

/** Highlighted entry for a book. A `kb` in the URL wins over the stored one (pass it as `fromUrl`). */
export function useHighlight(bookId: string, fromUrl?: string) {
  const stored = useSyncExternalStore(subscribeStores, () => getHighlight(bookId));
  const set = useCallback((entryId: string | null) => setHighlight(bookId, entryId), [bookId]);
  return [fromUrl ?? stored, set] as const;
}
