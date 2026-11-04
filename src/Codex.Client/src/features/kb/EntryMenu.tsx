import { useRef, useState } from 'react';
import type { KbDeleteImpact } from '@codex/core/api/kbEntries';
import { PromoteDialog } from './PromoteDialog';
import { DeleteEntryDialog } from './DeleteEntryDialog';
import { MergeEntryDialog } from './MergeEntryDialog';
import { kbTestIds } from './testIds';
import { useDeleteEntry } from './useDeleteEntry';
import { useMergeEntry } from './useMergeEntry';

interface EntryMenuProps {
  bookId: string;
  entryId: string;
  name: string;
  /** Called once the entry is deleted (the window closes). */
  onDeleted: () => void;
  /** Set when this is the book's own entry and the book is in a series: offers Move to series. */
  seriesName?: string;
  /** Called after the entry moved (or merged) into the series. `merged` means this entry is gone. */
  onPromoted?: (merged: boolean) => void;
}

// The header's ⋯ menu. Delete entry… opens the confirmation with the entry's use count.
export function EntryMenu({
  bookId,
  entryId,
  name,
  onDeleted,
  seriesName,
  onPromoted,
}: EntryMenuProps) {
  const [state, setState] = useState<'closed' | 'menu' | 'delete' | 'merge' | 'promote'>('closed');
  const [impact, setImpact] = useState<KbDeleteImpact | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const deletion = useDeleteEntry(bookId);
  const merge = useMergeEntry(bookId);
  const opener = useRef<HTMLButtonElement>(null);
  const closeMenu = () => {
    setState('closed');
    opener.current?.focus();
  };

  const askToDelete = () => {
    setState('delete');
    setImpact(null);
    setLoadError(null);
    deletion
      .impact(entryId)
      .then(setImpact, (e: unknown) =>
        setLoadError(e instanceof Error ? e.message : 'Could not load where the entry is used.'),
      );
  };

  return (
    <div className="relative">
      <button
        ref={opener}
        type="button"
        data-testid={kbTestIds.entryMenu}
        aria-label="More"
        aria-expanded={state !== 'closed'}
        onClick={() => setState(state === 'closed' ? 'menu' : 'closed')}
        className="h-7 w-7 rounded border-0 bg-transparent text-muted hover:bg-accent-soft"
      >
        ⋯
      </button>
      {state === 'menu' && (
        <div
          onKeyDown={(event) => {
            if (event.key !== 'Escape') return;
            event.stopPropagation();
            closeMenu();
          }}
          className="absolute right-0 z-30 mt-1 w-44 rounded border border-border bg-surface py-1 shadow-lg"
        >
          {seriesName !== undefined && (
            <button
              type="button"
              data-testid={kbTestIds.entryMenuPromote}
              onClick={() => setState('promote')}
              className="flex h-[var(--row-height)] w-full items-center border-0 bg-transparent px-3 text-left text-sm text-text hover:bg-accent-soft"
            >
              Move to series
            </button>
          )}
          <button
            type="button"
            data-testid={kbTestIds.entryMenuMerge}
            onClick={() => setState('merge')}
            className="flex h-[var(--row-height)] w-full items-center border-0 bg-transparent px-3 text-left text-sm text-text hover:bg-accent-soft"
          >
            Merge into…
          </button>
          <button
            type="button"
            data-testid={kbTestIds.entryMenuDelete}
            onClick={askToDelete}
            className="flex h-[var(--row-height)] w-full items-center border-0 bg-transparent px-3 text-left text-sm text-danger hover:bg-accent-soft"
          >
            Delete entry…
          </button>
        </div>
      )}
      {state === 'merge' && (
        <MergeEntryDialog
          bookId={bookId}
          entryId={entryId}
          name={name}
          onCancel={closeMenu}
          onMerge={(intoId, intoName) => merge(entryId, name, intoId, intoName, onDeleted)}
        />
      )}
      {state === 'promote' && seriesName !== undefined && (
        <PromoteDialog
          bookId={bookId}
          entryId={entryId}
          name={name}
          seriesName={seriesName}
          onCancel={closeMenu}
          onDone={(merged) => {
            setState('closed');
            onPromoted?.(merged);
          }}
        />
      )}
      {state === 'delete' && (
        <DeleteEntryDialog
          name={name}
          impact={impact}
          loadError={loadError}
          onCancel={closeMenu}
          onDelete={() => deletion.remove(entryId, name, onDeleted)}
        />
      )}
    </div>
  );
}
