import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { bookKey } from '@codex/core/api/books';
import { invalidateOutline } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import { showToast } from '@codex/core/editing/toastStore';
import { plural } from '@codex/core/features/plan/format';

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

/**
 * Rename, delete and restore one chapter. Delete archives it (its scenes keep their places, the prose is never touched,
 * so a `ch:` link keeps working) with Undo in the toast; the References badge follows because the uses leave clearance.
 */
export function useChapterOps() {
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

  /** Rejects with the server's reason, so the heading can show it inline. */
  const rename = useCallback(
    async (chapterId: string, title: string) => {
      await planApi.renameChapter(chapterId, title);
      await invalidateOutline(queryClient, bookId);
    },
    [bookId, queryClient],
  );

  const restore = useCallback(
    async (chapterId: string) => {
      await planApi.restoreChapter(chapterId);
      await refresh();
    },
    [refresh],
  );

  const restoreWithToast = useCallback(
    async (chapterId: string) => {
      try {
        await restore(chapterId);
        showToast({ message: 'Chapter restored' });
      } catch (error) {
        showToast({ message: `Could not restore the chapter: ${reasonOf(error)}` });
      }
    },
    [restore],
  );

  const archive = useCallback(
    async (chapterId: string) => {
      try {
        const result = await planApi.archiveChapter(chapterId);
        await refresh();
        const uses = result.archivedUses.length;
        showToast({
          message: `Chapter archived · ${plural(result.sceneCount, 'scene')}${uses === 0 ? '' : ` · ${plural(uses, 'source use')}`}`,
          undo: () => restoreWithToast(chapterId),
        });
      } catch (error) {
        showToast({ message: `Could not archive the chapter: ${reasonOf(error)}` });
      }
    },
    [refresh, restoreWithToast],
  );

  return { rename, archive, restore: restoreWithToast, restoreQuietly: restore };
}
