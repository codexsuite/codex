import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { invalidateOutline } from '@codex/core/api/outline';
import { planApi, type SceneUse } from '@codex/core/api/plan';
import { bookKey } from '@codex/core/api/books';
import { showToast } from '@codex/core/editing/toastStore';

const usesText = (uses: SceneUse[], verb: string) => {
  if (uses.length === 0) return '';
  const titles = [...new Set(uses.map((u) => u.sourceTitle))].join(', ');
  return ` · ${uses.length} source ${uses.length === 1 ? 'use' : 'uses'} ${verb} (${titles})`;
};

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

/** Archive and restore a scene. Both refresh the plan and the book header (the References badge follows clearance). */
export function useSceneArchive() {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();

  const refresh = useCallback(
    () =>
      Promise.all([
        invalidateOutline(queryClient, bookId),
        queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
      ]),
    [bookId, queryClient],
  );

  const restore = useCallback(
    async (sceneId: string) => {
      const result = await planApi.restoreScene(sceneId);
      await refresh();
      showToast({ message: `Scene restored${usesText(result.restoredUses, 'restored')}` });
    },
    [refresh],
  );

  const archive = useCallback(
    async (sceneId: string) => {
      try {
        const result = await planApi.archiveScene(sceneId);
        await refresh();
        showToast({
          message: `Scene archived${usesText(result.archivedUses, 'archived')}`,
          undo: () => restore(sceneId),
        });
      } catch (error) {
        showToast({ message: `Could not archive the scene: ${reasonOf(error)}` });
      }
    },
    [refresh, restore],
  );

  const restoreFromList = useCallback(
    async (sceneId: string) => {
      try {
        await restore(sceneId);
      } catch (error) {
        showToast({ message: `Could not restore the scene: ${reasonOf(error)}` });
      }
    },
    [restore],
  );

  // Merge into the previous scene of the chapter; the absorbed scene is archived, so Undo restores it (the text it
  // held stays in the earlier scene: version history brings the earlier text back).
  const merge = useCallback(
    async (sceneId: string) => {
      try {
        await planApi.mergeScene(sceneId);
        await refresh();
        showToast({ message: 'Scenes merged · the later scene is in Archived' });
      } catch (error) {
        showToast({ message: `Could not merge the scene: ${reasonOf(error)}` });
      }
    },
    [refresh],
  );

  return { archive, restore: restoreFromList, merge };
}
