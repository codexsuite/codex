import type { QueryClient } from '@tanstack/react-query';
import { bookKey } from '@codex/core/api/books';
import { sourcesCslKey, sourcesKey } from '@codex/core/api/referenceSources';
import { clearanceSummaryKey, markersKey } from '@codex/core/api/references';

/**
 * Refreshes everything a References write can change: the book header (the References badge on the book bar), the
 * clearance summary strip, the sources list, the bibliography and the markers Write shows.
 */
export function refreshClearance(queryClient: QueryClient, bookId: string): Promise<unknown> {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
    queryClient.invalidateQueries({ queryKey: clearanceSummaryKey(bookId) }),
    queryClient.invalidateQueries({ queryKey: sourcesKey(bookId) }),
    queryClient.invalidateQueries({ queryKey: sourcesCslKey(bookId) }),
    queryClient.invalidateQueries({ queryKey: markersKey(bookId) }),
  ]);
}
