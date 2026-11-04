import { useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { structureApi } from '@codex/core/api/structure';
import { setSceneSummary } from '@codex/core/api/write';
import { useAutosave } from '../../editing/useAutosave';
import { MarkdownSurface } from '../../prose';
import { useCanWrite } from '../../shell/bookAccess';
import { summaryKey, useSaveCoordinator } from './saveCoordinator';

interface SceneSummaryProps {
  sceneId: string;
  summary: string | null;
}

// The scene summary in the gutter, shared with Plan and saved by `PATCH /api/scenes/{id}`. That endpoint has no
// version, so the version `useAutosave` tracks is a local counter that only counts this editor's saves.
export function SceneSummary({ sceneId, summary }: SceneSummaryProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const coordinator = useSaveCoordinator();
  const key = summaryKey(sceneId);
  const store = useMemo(() => coordinator.store(key), [coordinator, key]);
  useEffect(() => () => coordinator.release(key), [coordinator, key]);

  const [text, setText] = useState(summary ?? '');
  const canWrite = useCanWrite();
  const saves = useRef(0);
  const { flush } = useAutosave({
    value: text,
    version: 0,
    store,
    save: async (value) => {
      const saved = await structureApi.patchScene(sceneId, {
        summary: value,
        title: null,
        status: null,
      });
      saves.current += 1;
      setSceneSummary(queryClient, bookId, sceneId, saved.summary);
      return { version: saves.current };
    },
  });

  return (
    <div onBlur={flush}>
      <MarkdownSurface
        surface="scene-summary"
        variant="summary"
        value={text}
        onChange={setText}
        readOnly={!canWrite}
        placeholder={canWrite ? 'Add a summary' : undefined}
      />
    </div>
  );
}
