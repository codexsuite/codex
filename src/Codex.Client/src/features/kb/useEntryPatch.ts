import { useQueryClient } from '@tanstack/react-query';
import { kbKey } from '@codex/core/api/kb';
import {
  kbEntryKey,
  patchKbEntry,
  type KbEntry,
  type PatchKbEntryRequest,
} from '@codex/core/api/kbEntries';
import { invalidateOutline } from '@codex/core/api/outline';

// PATCH one entry, put the answer in the entry's cache and refresh the sidebar (names, counts, categories) and, after a
// rename, the outline (scene cards name their linked entries).
export function useEntryPatch(bookId: string, entryId: string) {
  const queryClient = useQueryClient();
  return async (request: PatchKbEntryRequest): Promise<KbEntry> => {
    const entry = await patchKbEntry(entryId, request);
    queryClient.setQueryData(kbEntryKey(entryId), entry);
    await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
    if (request.name != null) await invalidateOutline(queryClient, bookId);
    return entry;
  };
}
