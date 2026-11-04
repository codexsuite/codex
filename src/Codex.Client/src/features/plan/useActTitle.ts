import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { outlineKey } from '@codex/core/api/outline';
import { planApi } from '@codex/core/api/plan';
import type { Outline, OutlineAct } from '@codex/core/api/structure';
import { useAutosave } from '../../editing/useAutosave';

/**
 * An act's title, edited in place and saved with PATCH /api/acts/{id} (3 s after the first edit, and on blur via
 * `flush`). As with a scene field, the PATCH has no version, so the version only counts this hook's own saves.
 */
export function useActTitle(act: OutlineAct) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const [value, setValue] = useState(act.title);
  const [version, setVersion] = useState(0);
  const { flush } = useAutosave({
    value,
    version,
    save: async (text, current) => {
      const updated = await planApi.renameAct(act.id, text);
      queryClient.setQueryData<Outline>(outlineKey(bookId), (outline) =>
        outline === undefined
          ? outline
          : {
              ...outline,
              acts: outline.acts.map((a) => (a.id === act.id ? { ...a, title: updated.title } : a)),
            },
      );
      setVersion(current + 1);
      return { version: current + 1 };
    },
  });
  return { value, setValue, flush };
}
