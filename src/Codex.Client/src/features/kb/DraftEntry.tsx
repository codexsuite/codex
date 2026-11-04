import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiRequestError } from '@codex/core/api/client';
import { kbKey } from '@codex/core/api/kb';
import { createKbEntry } from '@codex/core/api/kbEntries';
import { EntryFrame } from './EntryFrame';
import { kbTestIds } from './testIds';
import { useKbCategories } from './useKbCategories';

interface DraftEntryProps {
  bookId: string;
  categoryId: string;
  /** Called when the draft is closed without a name: nothing was written. */
  onDiscard: () => void;
  /** Called with the new entry's id once the name has created it. */
  onCreated: (entryId: string) => void;
}

// A new entry before it has a name. Nothing is written until a name is typed (Enter or leaving the field);
// closing the window with the name still empty leaves no row.
export function DraftEntry({ bookId, categoryId, onDiscard, onCreated }: DraftEntryProps) {
  const queryClient = useQueryClient();
  const categories = useKbCategories(bookId);
  const category = categories.find((c) => c.id === categoryId);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  // Another entry already has the name: the error offers Create anyway.
  const [taken, setTaken] = useState(false);
  const open = useRef(true);
  useEffect(() => {
    open.current = true;
    return () => {
      open.current = false;
    };
  }, []);

  const create = async (allowDuplicate = false) => {
    if (name.trim() === '' || creating) return;
    setCreating(true);
    try {
      const entry = await createKbEntry(bookId, {
        categoryId,
        name,
        allowDuplicate: allowDuplicate || undefined,
      });
      await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
      if (open.current) onCreated(entry.id);
    } catch (e) {
      setCreating(false);
      setError(e instanceof ApiRequestError ? e.message : 'The entry could not be created.');
      setTaken(e instanceof ApiRequestError && e.status === 409);
    }
  };

  return (
    <EntryFrame
      label="New entry"
      colour={category?.colour ?? null}
      expanded={false}
      onClose={onDiscard}
    >
      <header className="flex items-center gap-2 border-b border-border px-4 py-2">
        <span className="text-2xs font-medium tracking-[.02em] text-muted uppercase">
          New entry{category ? ` in ${category.name}` : ''}
        </span>
        <span className="flex-1" />
        <button
          type="button"
          data-testid={kbTestIds.entryWindowClose}
          aria-label="Close"
          onClick={onDiscard}
          className="h-7 w-7 rounded border-0 bg-transparent text-muted hover:bg-accent-soft"
        >
          ×
        </button>
      </header>
      <div className="p-4">
        <input
          autoFocus
          type="text"
          data-testid={kbTestIds.draftName}
          aria-label="Entry name"
          placeholder="Name"
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setTaken(false);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') void create();
          }}
          onBlur={(event) => {
            if (event.relatedTarget?.getAttribute('data-testid') !== kbTestIds.draftCreateAnyway)
              void create();
          }}
          className="h-8 w-full rounded border border-border bg-bg px-2 text-base font-semibold text-text"
        />
        {error && (
          <p
            data-testid={kbTestIds.draftError}
            role="alert"
            className="m-0 mt-2 text-xs text-danger"
          >
            {error}
            {taken && (
              <button
                type="button"
                data-testid={kbTestIds.draftCreateAnyway}
                onClick={() => void create(true)}
                className="ml-2 h-5 rounded-xs border border-border bg-transparent px-1.5 text-xs text-text hover:bg-surface-2"
              >
                Create anyway
              </button>
            )}
          </p>
        )}
      </div>
    </EntryFrame>
  );
}
