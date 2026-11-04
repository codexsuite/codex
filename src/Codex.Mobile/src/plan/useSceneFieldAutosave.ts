import { updateOutlineScene } from '@codex/core/api/outline';
import { structureApi, type OutlineScene } from '@codex/core/api/structure';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useAutosaveController } from '~/book/useAutosaveController';

/**
 * A scene's title or summary, edited in place and saved with `PATCH /api/scenes/{id}` as the desktop does: 3 s after
 * the first edit and on blur (`flush`), through the core's `AutosaveController` on its own source of the book's save status (`~/book/saveStatus`), so the
 * book bar's dot follows. The PATCH carries no version and has no 409, so `version` is only a local counter that rises
 * with each successful save and lets the controller tell its own saves from a reload. Leaving the sheet flushes what is
 * still waiting and keeps the controller until that save has gone through (an offline one waits for the connection), so
 * closing within the 3 s loses nothing.
 */
export function useSceneFieldAutosave(
  bookId: string,
  scene: OutlineScene,
  field: 'title' | 'summary',
) {
  const queryClient = useQueryClient();
  const server = scene[field] ?? '';

  const autosave = useAutosaveController<string>(bookId, 'plan', `${scene.id}:${field}`, {
    value: server,
    version: 0,
    save: async (text, current) => {
      const updated = await structureApi.patchScene(scene.id, {
        title: field === 'title' ? text : null,
        summary: field === 'summary' ? text : null,
        status: null,
      });
      // Only the saved field: a labels or entries change made meanwhile is not overwritten by the server's older copy.
      updateOutlineScene(queryClient, bookId, scene.id, (s) => ({ ...s, [field]: updated[field] }));
      setVersion(current + 1);
      return { version: current + 1 };
    },
  });
  const { controller, sync, pending } = autosave;
  // Text left unsent the last time this field was open (its controller is still trying, or gave up and kept a draft).
  const [value, setValue] = useState(pending?.value ?? server);
  const [version, setVersion] = useState(pending?.version ?? 0);

  useEffect(() => {
    sync(value, version);
  }, [sync, value, version]);

  return { value, setValue, flush: () => void controller.flush() };
}
