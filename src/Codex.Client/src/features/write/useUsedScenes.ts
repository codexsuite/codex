import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKbEntry, kbEntryKey } from '@codex/core/api/kbEntries';

const NONE: ReadonlySet<string> = new Set();

// The scenes that use an entry in any way (links, mentions, beats, additions...), from the entry's "Used in".
export function useUsedScenes(entryId: string | null): ReadonlySet<string> {
  const entry = useQuery({
    queryKey: kbEntryKey(entryId ?? ''),
    queryFn: ({ signal }) => getKbEntry(entryId!, { signal }),
    enabled: entryId !== null,
  });
  return useMemo(
    () => (entryId && entry.data ? new Set(entry.data.usedIn.map((u) => u.sceneId)) : NONE),
    [entryId, entry.data],
  );
}
