import { useQueryClient } from '@tanstack/react-query';
import { kbKey } from '@codex/core/api/kb';
import { kbEntryKey, mergeKbEntry } from '@codex/core/api/kbEntries';
import { invalidateOutline } from '@codex/core/api/outline';
import { manuscriptKey } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';

// Merge an entry into another. The merged entry is archived (the window closes and its query is dropped); scenes, the
// outline and the lists are read again because mentions and links now point at the target.
export function useMergeEntry(bookId: string) {
  const queryClient = useQueryClient();
  return async (
    entryId: string,
    name: string,
    intoId: string,
    intoName: string,
    afterMerge: () => void,
  ) => {
    await mergeKbEntry(entryId, intoId);
    showToast({ message: `Merged ${name} into ${intoName}` });
    afterMerge();
    queryClient.removeQueries({ queryKey: kbEntryKey(entryId) });
    await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
    await queryClient.invalidateQueries({ queryKey: ['kb-entry'] });
    await queryClient.invalidateQueries({ queryKey: manuscriptKey(bookId) });
    await invalidateOutline(queryClient, bookId);
  };
}
