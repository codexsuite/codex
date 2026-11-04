import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useAutosave } from '../../editing/useAutosave';
import { structureApi, type OutlineScene } from '@codex/core/api/structure';
import { updateOutlineScene } from '@codex/core/api/outline';

/**
 * A scene's summary or title, edited in place and saved with PATCH /api/scenes/{id} (3 s after the first edit, and on
 * blur via `flush`). The PATCH carries no version and has no 409, so the version is only a local counter that
 * rises with each successful save: it lets useAutosave tell its own saves from a reload.
 */
export function useSceneField(scene: OutlineScene, field: 'summary' | 'title') {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(scene[field] ?? '');
  const [version, setVersion] = useState(0);
  const { flush } = useAutosave({
    value,
    version,
    save: async (text, current) => {
      const updated = await structureApi.patchScene(scene.id, {
        summary: field === 'summary' ? text : null,
        title: field === 'title' ? text : null,
        status: null,
      });
      updateOutlineScene(queryClient, bookId, scene.id, () => updated);
      setVersion(current + 1);
      return { version: current + 1 };
    },
  });
  return { value, setValue, flush };
}
