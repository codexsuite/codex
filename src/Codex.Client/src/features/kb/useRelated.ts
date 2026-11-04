import { useQueryClient } from '@tanstack/react-query';
import { addKbRelated, kbEntryKey, removeKbRelated, type KbEntry } from '@codex/core/api/kbEntries';
import { showToast } from '@codex/core/editing/toastStore';

// Link and unlink related entries. Both entries' windows change, so the cached entries are refreshed; each change
// can be undone from its toast by doing the opposite.
export function useRelated(entry: KbEntry) {
  const queryClient = useQueryClient();
  const refresh = (updated: KbEntry, otherId: string) => {
    queryClient.setQueryData(kbEntryKey(entry.id), updated);
    void queryClient.invalidateQueries({ queryKey: kbEntryKey(otherId) });
  };

  const add = async (otherId: string, otherName: string) => {
    refresh(await addKbRelated(entry.id, otherId), otherId);
    showToast({
      message: `Linked ${entry.name} and ${otherName}`,
      undo: async () => refresh(await removeKbRelated(entry.id, otherId), otherId),
    });
  };

  const remove = async (otherId: string, otherName: string) => {
    refresh(await removeKbRelated(entry.id, otherId), otherId);
    showToast({
      message: `Unlinked ${otherName}`,
      undo: async () => refresh(await addKbRelated(entry.id, otherId), otherId),
    });
  };

  return { add, remove };
}
