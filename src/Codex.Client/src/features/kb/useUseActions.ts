import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { linkSceneEntry, unlinkSceneEntry } from '@codex/core/api/kb';
import { kbEntryKey, makeKbMention, type KbUse } from '@codex/core/api/kbEntries';
import { invalidateOutline } from '@codex/core/api/outline';
import { manuscriptKey } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';

// The Used in row buttons: Make mention (the server wraps the first plain use in the scene) and Unlink (a scene
// linked to the entry). Each refreshes the entry's window, and the scene's prose or its book's outline (linked
// entries); a failure is kept for the tab to show.
export function useUseActions(entryId: string) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const refresh = async (use: KbUse) => {
    await queryClient.invalidateQueries({ queryKey: kbEntryKey(entryId) });
    await invalidateOutline(queryClient, use.bookId);
  };

  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That change could not be saved.');
    }
  };

  return {
    error,
    makeMention: (use: KbUse) =>
      run(async () => {
        queryClient.setQueryData(kbEntryKey(entryId), await makeKbMention(entryId, use.sceneId));
        void queryClient.invalidateQueries({ queryKey: manuscriptKey(use.bookId) });
        showToast({ message: `Made a mention in scene ${use.number}` });
      }),
    unlink: (use: KbUse) =>
      run(async () => {
        await unlinkSceneEntry(use.sceneId, entryId);
        await refresh(use);
        showToast({
          message: `Unlinked scene ${use.number}`,
          undo: async () => {
            await linkSceneEntry(use.sceneId, entryId);
            await refresh(use);
          },
        });
      }),
  };
}
