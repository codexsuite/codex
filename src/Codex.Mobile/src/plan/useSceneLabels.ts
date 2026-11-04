import { outlineKey, updateOutlineScene } from '@codex/core/api/outline';
import {
  structureApi,
  type LabelRef,
  type Outline,
  type OutlineScene,
} from '@codex/core/api/structure';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef } from 'react';
import { NEW_LABEL_COLOR } from './labelTone';
import { findScene } from './planModel';

const labelsKey = (bookId: string) => ['labels', bookId] as const;

/**
 * The book's labels (its own and its series') and the writes that change a scene's. A change goes into the cached outline
 * first (so a second tap before the server answers builds on it, not on the old selection), then to the server; a
 * failure puts the previous labels back. The server's answer replaces the optimistic labels only when no newer change
 * is in flight.
 */
export function useSceneLabels(bookId: string, scene: OutlineScene) {
  const queryClient = useQueryClient();
  const latest = useRef(0);
  const labels = useQuery({
    queryKey: labelsKey(bookId),
    queryFn: ({ signal }) => structureApi.labels(bookId, { signal }),
  });

  const current = (): LabelRef[] =>
    findScene(
      queryClient.getQueryData<Outline>(outlineKey(bookId)) ?? { bookId, acts: [] },
      scene.id,
    )?.labels ?? scene.labels;
  const put = (next: LabelRef[]) =>
    updateOutlineScene(queryClient, bookId, scene.id, (s) => ({ ...s, labels: next }));

  const save = useMutation({
    mutationFn: async (next: LabelRef[]) => {
      const before = current();
      const mine = ++latest.current;
      put(next);
      try {
        const saved = await structureApi.setSceneLabels(scene.id, {
          labelIds: next.map((l) => l.id),
        });
        if (mine === latest.current) put(saved);
      } catch (error) {
        if (mine === latest.current) put(before);
        throw error;
      }
    },
  });

  /** Adds the label to the scene's labels, or takes it off. */
  const toggle = (id: string) => {
    const selected = current();
    if (selected.some((l) => l.id === id)) {
      save.mutate(selected.filter((l) => l.id !== id));
      return;
    }
    const known = labels.data?.find((l) => l.id === id);
    if (known !== undefined)
      save.mutate([...selected, { id, name: known.name, color: known.color }]);
  };

  const create = useMutation({
    mutationFn: async (name: string) => {
      const made = await structureApi.createLabel(bookId, { name, color: NEW_LABEL_COLOR });
      await queryClient.invalidateQueries({ queryKey: labelsKey(bookId) });
      await save.mutateAsync([...current(), { id: made.id, name: made.name, color: made.color }]);
    },
  });

  return { labels, toggle, save, create };
}
