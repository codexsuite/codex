import { useQueryClient } from '@tanstack/react-query';
import { kbEntryKey } from '@codex/core/api/kbEntries';
import { additionApi, additionsKey, setSceneProse, writeApi } from '@codex/core/api/write';
import { showToast } from '@codex/core/editing/toastStore';

const EMPTY_NOTE = 'Write the note first.';

// Timeline notes are Codex additions, so they are written through the same endpoints as Write's: an add puts an
// `::kb-addition` line at the start of the chosen scene's prose, an edit changes the addition's text. Each refreshes
// the entry's window (its timeline) and the book's additions, and the scene's prose where it changed.
export function useTimelineNotes(bookId: string, entryId: string) {
  const queryClient = useQueryClient();
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: kbEntryKey(entryId) });
    await queryClient.invalidateQueries({ queryKey: additionsKey(bookId) });
  };

  return {
    add: async (sceneId: string, sceneNumber: string, text: string) => {
      const body = text.trim();
      if (body === '') throw new Error(EMPTY_NOTE);
      // The scene's prose as the server holds it now, so the insert is not a stale write.
      const manuscript = await writeApi.manuscript(bookId);
      const scene = manuscript.scenes.find((s) => s.sceneId === sceneId);
      if (!scene) throw new Error('That scene is not in the book.');
      const additionId = crypto.randomUUID();
      const line = `::kb-addition{id="${additionId}"}`;
      const sceneBody = scene.body.trim() === '' ? `${line}\n` : `${line}\n\n${scene.body}`;
      const result = await additionApi.insert(sceneId, {
        additionId,
        entryId,
        body,
        sceneBody,
        version: scene.version,
      });
      setSceneProse(queryClient, bookId, { sceneId, body: sceneBody, version: result.version });
      await refresh();
      showToast({ message: `Added a note at scene ${sceneNumber}` });
    },
    edit: async (noteId: string, previous: string, text: string) => {
      const body = text.trim();
      if (body === '') throw new Error(EMPTY_NOTE);
      await additionApi.patch(noteId, { body });
      await refresh();
      showToast({
        message: 'Timeline note changed',
        undo: async () => {
          await additionApi.patch(noteId, { body: previous });
          await refresh();
        },
      });
    },
  };
}
