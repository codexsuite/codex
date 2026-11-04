import type { QueryClient } from '@tanstack/query-core';
import type { Outline, OutlineScene } from './structure';

// One cache entry per book for `GET /api/books/{id}/outline`, read by Plan, Write and anything else that shows acts,
// chapters and scenes. A write that changes the outline either puts the server's answer into this entry
// (`setOutline`, `updateOutlineScene`) or invalidates it (`invalidateOutline`, which also covers keys nested under it).
export const outlineKey = (bookId: string) => ['outline', bookId] as const;

export function invalidateOutline(queryClient: QueryClient, bookId: string): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: outlineKey(bookId) });
}

/** Replaces one scene in the cached outline (a no-op when the outline is not cached or the scene is not in it). */
export function updateOutlineScene(
  queryClient: QueryClient,
  bookId: string,
  sceneId: string,
  update: (scene: OutlineScene) => OutlineScene,
): void {
  queryClient.setQueryData<Outline>(outlineKey(bookId), (outline) =>
    outline === undefined
      ? outline
      : {
          ...outline,
          acts: outline.acts.map((act) => ({
            ...act,
            chapters: act.chapters.map((chapter) => ({
              ...chapter,
              scenes: chapter.scenes.map((s) => (s.id === sceneId ? update(s) : s)),
            })),
          })),
        },
  );
}
