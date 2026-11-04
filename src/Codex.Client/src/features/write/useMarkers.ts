import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getMarkers, markersKey, type Marker } from '@codex/core/api/references';

// The book's source markers, read once per book (the scenes and the pending drafts share the cache entry).
export function useMarkers(bookId: string) {
  const query = useQuery({
    queryKey: markersKey(bookId),
    queryFn: ({ signal }) => getMarkers(bookId, { signal }),
    enabled: bookId !== '',
  });
  const byUse = useMemo(
    () => new Map<string, Marker>((query.data ?? []).map((m) => [m.useId, m])),
    [query.data],
  );
  return { markers: query.data, byUse };
}
