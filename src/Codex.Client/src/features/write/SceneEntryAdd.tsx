import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { linkSceneEntry } from '@codex/core/api/kb';
import { invalidateOutline } from '@codex/core/api/outline';
import type { EntryRef } from '@codex/core/api/structure';
import { showToast } from '@codex/core/editing/toastStore';
import { EntryPicker } from './EntryPicker';
import { PlusIcon } from './icons';
import { writeTestIds } from './testIds';
import { useMentionEntries } from './useMentionEntries';

interface SceneEntryAddProps {
  sceneId: string;
  linked: readonly EntryRef[];
}

// "+ Codex" in the scene gutter: pick a Knowledge base entry to link to the scene (as dropping one from the
// sidebar does).
export function SceneEntryAdd({ sceneId, linked }: SceneEntryAddProps) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const entries = useMentionEntries(bookId);
  const [at, setAt] = useState<{ left: number; bottom: number } | null>(null);
  const offered = entries.filter((e) => !linked.some((l) => l.id === e.id));

  const link = async (entryId: string, name: string) => {
    setAt(null);
    try {
      await linkSceneEntry(sceneId, entryId);
      await invalidateOutline(queryClient, bookId);
    } catch (error) {
      showToast({
        message: `Could not link ${name}: ${error instanceof Error ? error.message : 'Something went wrong.'}`,
      });
    }
  };

  return (
    <>
      <button
        type="button"
        data-testid={writeTestIds.sceneEntryAdd(sceneId)}
        title="Link a Knowledge base entry to this scene"
        aria-expanded={at !== null}
        className="write-mini"
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect();
          setAt((open) => (open ? null : { left: rect.left, bottom: rect.bottom }));
        }}
      >
        <PlusIcon />
        Codex
      </button>
      {at && (
        <EntryPicker
          entries={offered}
          rect={at}
          placeholder="Link an entry…"
          onPick={(entry) => void link(entry.id, entry.name)}
          onClose={() => setAt(null)}
        />
      )}
    </>
  );
}
