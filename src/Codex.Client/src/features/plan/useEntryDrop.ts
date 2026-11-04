import { useQueryClient } from '@tanstack/react-query';
import { useState, type DragEvent } from 'react';
import { useParams } from 'react-router-dom';
import { linkSceneEntry } from '@codex/core/api/kb';
import { invalidateOutline } from '@codex/core/api/outline';
import { showToast } from '@codex/core/editing/toastStore';
import { hasEntryDragData, readEntryDragData } from '../../shell/dragPayload';

/** Props for a card that takes a Knowledge base entry dragged from the sidebar; `over` is true while one hovers. */
export function useEntryDrop(sceneId: string) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const [over, setOver] = useState(false);

  const handlers = {
    onDragOver: (event: DragEvent) => {
      if (!hasEntryDragData(event.dataTransfer)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = 'link';
      setOver(true);
    },
    onDragLeave: () => setOver(false),
    onDrop: (event: DragEvent) => {
      setOver(false);
      const payload = readEntryDragData(event.dataTransfer);
      if (payload === null) return;
      event.preventDefault();
      void linkSceneEntry(sceneId, payload.entryId)
        .then(() => invalidateOutline(queryClient, bookId))
        .catch((error: unknown) =>
          showToast({
            message: `Could not link ${payload.name}: ${error instanceof Error ? error.message : 'Something went wrong.'}`,
          }),
        );
    },
  };
  return { over, handlers };
}
