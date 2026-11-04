import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { outlineKey } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import type { Outline } from '@codex/core/api/structure';
import { showToast } from '@codex/core/editing/toastStore';
import { applyMove, type Move } from '@codex/core/features/plan/moveOutline';

const requestOf = (move: Move) =>
  move.kind === 'act'
    ? planApi.moveAct(move.id, move.afterId)
    : move.kind === 'chapter'
      ? planApi.moveChapter(move.id, move.actId, move.afterId)
      : planApi.moveScene(move.id, move.chapterId, move.afterId);

// Reorders on screen at once, then asks the server; the server's outline replaces the guess, and a failed write puts
// the previous outline back and says why.
export function useReorder(bookId: string) {
  const queryClient = useQueryClient();
  return useCallback(
    async (move: Move) => {
      const key = outlineKey(bookId);
      await queryClient.cancelQueries({ queryKey: key, exact: true });
      const previous = queryClient.getQueryData<Outline>(key);
      if (!previous) return;
      queryClient.setQueryData(key, applyMove(previous, move));
      try {
        queryClient.setQueryData(key, await requestOf(move));
      } catch (error) {
        queryClient.setQueryData(key, previous);
        const reason = error instanceof Error ? error.message : 'Something went wrong.';
        showToast({ message: `Could not move the ${move.kind}: ${reason}` });
      }
    },
    [bookId, queryClient],
  );
}
