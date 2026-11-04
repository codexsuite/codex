import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { bookKey } from '@codex/core/api/books';
import { invalidateOutline } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import { showToast } from '@codex/core/editing/toastStore';

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

/**
 * The act menu's Delete act: archives the act (its chapters and scenes go with it) with Undo in the toast. Both refresh
 * the plan and the book header, since the act's source uses leave the clearance with it.
 */
export function useActArchive() {
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
    async (actId: string) => {
      try {
        await planApi.restoreAct(actId);
        await refresh();
        showToast({ message: 'Act restored' });
      } catch (error) {
        showToast({ message: `Could not restore the act: ${reasonOf(error)}` });
      }
    },
    [refresh],
  );

  const archive = useCallback(
    async (actId: string) => {
      try {
        const result = await planApi.archiveAct(actId);
        await refresh();
        const uses = result.archivedUses.length;
        showToast({
          message: `Act deleted${uses === 0 ? '' : ` · ${uses} source ${uses === 1 ? 'use' : 'uses'} archived`}`,
          undo: () => restore(actId),
        });
      } catch (error) {
        showToast({ message: `Could not delete the act: ${reasonOf(error)}` });
      }
    },
    [refresh, restore],
  );

  return { archive };
}
