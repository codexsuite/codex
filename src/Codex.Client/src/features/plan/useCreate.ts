import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { bookKey } from '@codex/core/api/books';
import { invalidateOutline } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import { showToast } from '@codex/core/editing/toastStore';
import { usePlanEdit } from './usePlanEdit';

const reasonOf = (error: unknown) =>
  error instanceof Error ? error.message : 'Something went wrong.';

/**
 * Adds an act to the end of the book (its title takes focus), a chapter to the end of an act, or a scene to the end of
 * a chapter (the new scene's summary takes focus).
 */
export function useCreate() {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const { setFocusSceneId, setFocusActId } = usePlanEdit();

  const refresh = useCallback(
    () =>
      Promise.all([
        invalidateOutline(queryClient, bookId),
        queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
      ]),
    [bookId, queryClient],
  );

  /** Resolves to the new act, or null when the server refused it (a toast says why). */
  const createAct = useCallback(async () => {
    try {
      const act = await planApi.createAct(bookId, { title: null, afterId: null });
      await refresh();
      setFocusActId(act.id);
      return act;
    } catch (error) {
      showToast({ message: `Could not add the act: ${reasonOf(error)}` });
      return null;
    }
  }, [bookId, refresh, setFocusActId]);

  const createChapter = useCallback(
    async (actId: string) => {
      try {
        await planApi.createChapter(actId, { title: null, afterId: null });
        await refresh();
      } catch (error) {
        showToast({ message: `Could not add the chapter: ${reasonOf(error)}` });
      }
    },
    [refresh],
  );

  const createScene = useCallback(
    async (chapterId: string) => {
      try {
        const scene = await planApi.createScene(chapterId, { title: null, afterId: null });
        await refresh();
        setFocusSceneId(scene.id);
      } catch (error) {
        showToast({ message: `Could not add the scene: ${reasonOf(error)}` });
      }
    },
    [refresh, setFocusSceneId],
  );

  return { createAct, createChapter, createScene };
}
