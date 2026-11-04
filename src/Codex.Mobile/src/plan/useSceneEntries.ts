import { kbKey, linkSceneEntry, unlinkSceneEntry } from '@codex/core/api/kb';
import { kbEntryKey } from '@codex/core/api/kbEntries';
import { updateOutlineScene } from '@codex/core/api/outline';
import type { OutlineScene } from '@codex/core/api/structure';
import { useMutation, useQueryClient } from '@tanstack/react-query';

/**
 * Link and unlink Knowledge base entries on a scene. The cached outline follows, and so do what shows the link from the
 * entry's side: the entries' scene counts and an open entry's Used in.
 */
export function useSceneEntries(bookId: string, scene: OutlineScene) {
  const queryClient = useQueryClient();
  const changed = (entryId: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: kbKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: kbEntryKey(entryId) }),
    ]);

  const link = useMutation({
    mutationFn: async (entry: { id: string; name: string }) => {
      await linkSceneEntry(scene.id, entry.id);
      updateOutlineScene(queryClient, bookId, scene.id, (s) =>
        s.linkedEntries.some((e) => e.id === entry.id)
          ? s
          : { ...s, linkedEntries: [...s.linkedEntries, { id: entry.id, name: entry.name }] },
      );
      await changed(entry.id);
    },
  });

  const unlink = useMutation({
    mutationFn: async (entryId: string) => {
      await unlinkSceneEntry(scene.id, entryId);
      updateOutlineScene(queryClient, bookId, scene.id, (s) => ({
        ...s,
        linkedEntries: s.linkedEntries.filter((e) => e.id !== entryId),
      }));
      await changed(entryId);
    },
  });

  return { link, unlink };
}
