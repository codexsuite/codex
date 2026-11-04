import { kbKey } from '@codex/core/api/kb';
import {
  kbEntryKey,
  patchKbEntry,
  type KbEntry,
  type PatchKbEntryRequest,
} from '@codex/core/api/kbEntries';
import { useQueryClient } from '@tanstack/react-query';

/** PATCH one entry, put the answer in the entry's cache and refresh the list (names, counts, Always) behind it. */
export function useEntryPatch(bookId: string, entryId: string) {
  const queryClient = useQueryClient();
  return async (request: PatchKbEntryRequest): Promise<KbEntry> => {
    const entry = await patchKbEntry(entryId, request);
    queryClient.setQueryData(kbEntryKey(entryId), entry);
    await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
    return entry;
  };
}
