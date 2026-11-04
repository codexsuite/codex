import { useQueryClient } from '@tanstack/react-query';
import { ApiRequestError } from '@codex/core/api/client';
import { kbKey } from '@codex/core/api/kb';
import {
  deleteKbEntry,
  kbEntryKey,
  getKbDeleteImpact,
  restoreKbEntry,
  type KbDeleteImpact,
  type KbUndoRecord,
} from '@codex/core/api/kbEntries';
import { invalidateOutline } from '@codex/core/api/outline';
import { manuscriptKey } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';

const CHANGED =
  'Could not undo: a scene or text changed after the entry was deleted, so it stays deleted.';

// Delete an entry with the counts shown first, and put it back from the toast. Undo sends the record the delete
// returned; if something changed since, the server answers 409 and the toast says so. Both rewrite prose, summaries
// and scene links, so the book's manuscript and outline are read again too.
export function useDeleteEntry(bookId: string) {
  const queryClient = useQueryClient();
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
    await queryClient.invalidateQueries({ queryKey: ['kb-entry'] });
    await queryClient.invalidateQueries({ queryKey: manuscriptKey(bookId) });
    await invalidateOutline(queryClient, bookId);
  };

  const undo = async (record: KbUndoRecord) => {
    try {
      await restoreKbEntry(record);
    } catch (e) {
      if (e instanceof ApiRequestError && e.status === 409) {
        showToast({ message: CHANGED });
        return;
      }
      throw e;
    }
    await refresh();
  };

  return {
    impact: (entryId: string): Promise<KbDeleteImpact> => getKbDeleteImpact(entryId),
    // `afterDelete` closes the entry's window and the deleted entry's query is dropped before the lists refresh:
    // refetching it would be a 404 that retries.
    remove: async (entryId: string, name: string, afterDelete: () => void) => {
      const record = await deleteKbEntry(entryId);
      showToast({ message: `Deleted ${name}`, undo: () => undo(record) });
      afterDelete();
      queryClient.removeQueries({ queryKey: kbEntryKey(entryId) });
      await refresh();
    },
  };
}
