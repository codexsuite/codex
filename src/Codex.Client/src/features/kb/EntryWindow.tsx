import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKbEntry, kbEntryKey } from '@codex/core/api/kbEntries';
import { ErrorState } from '../../shell/ErrorState';
import { DraftEntry } from './DraftEntry';
import { EntryEditor, type TabName } from './EntryEditor';
import { EntryFrame } from './EntryFrame';
import { kbTestIds } from './testIds';
import { useEntryHash } from './useEntryHash';

interface EntryWindowProps {
  bookId: string;
  /** The entry in `#entry=`, when there is one. */
  entryId?: string;
  /** Set while a new entry is being started in this category: nothing is written yet. */
  draftCategoryId?: string | null;
  onDraftClose?: () => void;
}

// The entry window over Plan and Write. Close (button or Esc) removes `#entry=` and keeps the other hash keys.
export function EntryWindow({ bookId, entryId, draftCategoryId, onDraftClose }: EntryWindowProps) {
  const entryHash = useEntryHash();
  if (draftCategoryId) {
    return (
      <DraftEntry
        bookId={bookId}
        categoryId={draftCategoryId}
        onDiscard={() => onDraftClose?.()}
        onCreated={(id) => {
          onDraftClose?.();
          entryHash.open(id);
        }}
      />
    );
  }
  return entryId === undefined ? null : (
    <LoadedEntry bookId={bookId} entryId={entryId} onClose={entryHash.close} />
  );
}

interface LoadedEntryProps {
  bookId: string;
  entryId: string;
  onClose: () => void;
}

function LoadedEntry({ bookId, entryId, onClose }: LoadedEntryProps) {
  const entryHash = useEntryHash();
  // Held here rather than in the editor, which is remounted per entry: stepping keeps the tab and the window size.
  const [tab, setTab] = useState<TabName>('entry');
  const [expanded, setExpanded] = useState(false);
  const { data, error } = useQuery({
    queryKey: kbEntryKey(entryId),
    queryFn: ({ signal }) => getKbEntry(entryId, { signal }),
  });
  if (data) {
    return (
      <EntryEditor
        key={data.id}
        bookId={bookId}
        entry={data}
        tab={tab}
        expanded={expanded}
        onTab={setTab}
        onExpanded={setExpanded}
        onStep={entryHash.open}
        onClose={onClose}
      />
    );
  }
  return (
    <EntryFrame label="Entry" colour={null} expanded={expanded} onClose={onClose}>
      <header className="flex items-center gap-2 border-b border-border px-4 py-2">
        <h2
          data-testid={kbTestIds.entryWindowTitle}
          className="m-0 min-w-0 flex-1 truncate text-base font-semibold"
        >
          Entry
        </h2>
        <button
          type="button"
          data-testid={kbTestIds.entryWindowClose}
          aria-label="Close"
          onClick={onClose}
          className="h-7 w-7 rounded border-0 bg-transparent text-muted hover:bg-accent-soft"
        >
          ×
        </button>
      </header>
      {error && (
        <ErrorState
          testId={kbTestIds.entryLoadError}
          title="The entry could not be loaded"
          detail={error.message}
        />
      )}
    </EntryFrame>
  );
}
