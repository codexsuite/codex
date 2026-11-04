import { useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { Editor } from '@tiptap/react';
import { useParams } from 'react-router-dom';
import { ApiRequestError } from '@codex/core/api/client';
import type { Marker } from '@codex/core/api/references';
import { setSceneProse } from '@codex/core/api/write';
import { serialize } from '../../prose';
import { showToast } from '@codex/core/editing/toastStore';
import { refreshClearance } from '../references/refreshClearance';
import { citationsApi } from './citations';
import { occurrencesUpTo, passageAt } from './citePassage';
import { passageOf, type SavedSelection } from './citeReturn';
import { useSaveCoordinator } from './saveCoordinator';
import { useSceneRegistry } from './sceneRegistry';

export interface Citations {
  /** Cites `sourceId` for the words at `range` in the scene's editor. */
  cite(
    sceneId: string,
    editor: Editor,
    range: { from: number; to: number },
    sourceId: string,
  ): Promise<boolean>;
  /**
   * Cites `sourceId` for a selection saved before the New source… detour; refused (a toast) when the scene's prose
   * has moved on since. Resolves to the new use's id, or null.
   */
  citeSaved(sceneId: string, selection: SavedSelection, sourceId: string): Promise<string | null>;
  /** Removes a marker: the words stay, the use is archived. */
  remove(marker: Pick<Marker, 'useId' | 'sceneId'>): Promise<boolean>;
}

// Citing and removing source markers. Each settles the scene's autosave, sends the version it saved, writes the
// server's new body to the cache (which remounts the editor) and refreshes the markers, sources and clearance
// summary, which is how every later marker gets its new number. A refused call shows its reason in a toast; a stale
// version also shows the scene's conflict. Both resolve to whether the change was made.
export const cannotMark =
  'These words cannot take a source marker: keep them inside one paragraph, outside code.';
const movedOn =
  'The scene changed while you were away, so select the words and cite the source again.';

export function useCitations(): Citations {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const coordinator = useSaveCoordinator();
  const registry = useSceneRegistry();

  return useMemo(() => {
    const guarded = async (
      sceneId: string,
      work: (saved: {
        body: string;
        version: number;
      }) => Promise<{ body: string; version: number; useId: string }>,
    ): Promise<string | null> => {
      const scene = registry.get(sceneId);
      if (!scene) {
        showToast({ message: 'Show the scene in Write to change its markers.' });
        return null;
      }
      try {
        if (!(await scene.settle())) return null;
        const result = await work(scene.saved());
        setSceneProse(queryClient, bookId, { sceneId, body: result.body, version: result.version });
        await refreshClearance(queryClient, bookId);
        return result.useId;
      } catch (error) {
        if (error instanceof ApiRequestError && error.status === 409) {
          coordinator.store(sceneId).set('conflict');
        }
        showToast({
          message: error instanceof Error ? error.message : 'The source marker could not be saved.',
        });
        return null;
      }
    };

    const remove = async (marker: Pick<Marker, 'useId' | 'sceneId'>) =>
      (await guarded(marker.sceneId, (saved) =>
        citationsApi.remove(marker.useId, saved.version),
      )) !== null;

    return {
      cite: async (sceneId, editor, range, sourceId) =>
        (await guarded(sceneId, async (saved) => {
          const found = passageAt(editor.state.doc, range.from, range.to);
          if (!found) throw new Error(cannotMark);
          // The occurrence is counted in the editor's Markdown and the server counts in the stored body. Settling
          // saves any edit (so the two agree after typing `/`); a body stored in another form that the editor never
          // rewrote is trusted only when the passage appears in it once.
          if (saved.body !== serialize(editor.state.doc)) {
            if (occurrencesUpTo(saved.body, found.passage) !== 1) throw new Error(cannotMark);
            found.occurrence = 1;
          }
          return citationsApi.cite({ sceneId, sourceId, version: saved.version, ...found });
        })) !== null,
      citeSaved: (sceneId, selection, sourceId) =>
        guarded(sceneId, async (saved) => {
          if (saved.version !== selection.version || selection.end > saved.body.length) {
            throw new Error(movedOn);
          }
          const found = passageOf(saved.body, selection);
          return citationsApi.cite({ sceneId, sourceId, version: saved.version, ...found });
        }),
      remove,
    };
  }, [bookId, queryClient, coordinator, registry]);
}
