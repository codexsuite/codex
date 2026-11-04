import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { linkSceneEntry } from '@codex/core/api/kb';
import { invalidateOutline } from '@codex/core/api/outline';
import type { OutlineScene } from '@codex/core/api/structure';
import { showToast } from '@codex/core/editing/toastStore';
import { filterEntries } from '../../prose/entries';
import { PlusIcon } from './icons';
import { miniButton } from './miniButton';
import { planTestIds } from './testIds';
import { usePlanEdit } from './usePlanEdit';

// `+ Codex` on a card: pick a Knowledge base entry to link to the scene (what dropping one from the sidebar does).
export function SceneEntryAdd({ scene }: { scene: OutlineScene }) {
  const { bookId = '' } = useParams();
  const queryClient = useQueryClient();
  const { entries } = usePlanEdit();
  const [at, setAt] = useState<{ left: number; bottom: number } | null>(null);
  const [query, setQuery] = useState('');
  const offered = entries.filter((e) => !scene.linkedEntries.some((l) => l.id === e.id));
  const shown = filterEntries(offered, query);

  const close = () => {
    setAt(null);
    setQuery('');
  };
  const link = async (entryId: string, name: string) => {
    close();
    try {
      await linkSceneEntry(scene.id, entryId);
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
        data-testid={planTestIds.sceneEntryAdd(scene.id)}
        title="Link a Knowledge base entry to this scene"
        aria-expanded={at !== null}
        className={miniButton}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect();
          setAt((open) => (open ? null : { left: rect.left, bottom: rect.bottom }));
        }}
      >
        <PlusIcon />
        Codex
      </button>
      {at && (
        <div
          data-testid={planTestIds.sceneEntryPicker}
          className="prose-popup"
          style={{ position: 'fixed', left: at.left, top: at.bottom + 4 }}
          onKeyDown={(e) => {
            if (e.key === 'Escape') close();
          }}
        >
          <input
            autoFocus
            aria-label="Link an entry…"
            placeholder="Link an entry…"
            className="w-full border-b border-border bg-transparent px-2 py-1 text-sm outline-none"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && shown[0]) void link(shown[0].id, shown[0].name);
            }}
          />
          {shown.map((entry) => (
            <button
              key={entry.id}
              type="button"
              data-testid={planTestIds.sceneEntryOption(entry.id)}
              className="prose-popup-item w-full text-left"
              onClick={() => void link(entry.id, entry.name)}
            >
              <span>{entry.name}</span>
              <span className="prose-popup-meta">{entry.category}</span>
            </button>
          ))}
          {shown.length === 0 && <div className="prose-popup-empty">No matching entry</div>}
        </div>
      )}
    </>
  );
}
