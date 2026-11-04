import { bookKey } from '@codex/core/api/books';
import { invalidateOutline } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import type { OutlineAct } from '@codex/core/api/structure';
import { liveChapters } from '@codex/core/features/plan/format';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { sceneSheetLink } from '~/links';

/**
 * `+ New chapter` at the end of an act: the chapter after the act's last live one, then its first scene (the server
 * makes a chapter with none, and the design opens the new scene), then the outline is read again and the new scene's
 * sheet opens. If the scene fails the chapter is still there, so the outline is read again either way.
 */
export function useCreateChapter(bookId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (act: OutlineAct) => {
      try {
        const last = liveChapters(act).at(-1);
        const chapter = await planApi.createChapter(act.id, {
          title: null,
          afterId: last?.id ?? null,
        });
        return await planApi.createScene(chapter.id, { title: null, afterId: null });
      } finally {
        await Promise.all([
          invalidateOutline(queryClient, bookId),
          queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
        ]);
      }
    },
    onSuccess: (scene) => router.push(sceneSheetLink(bookId, scene.id)),
  });
}
