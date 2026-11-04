import { useState } from 'react';
import type { MentionEntry } from '../../prose';
import { filterEntries } from '../../prose/entries';
import { writeTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface EntryPickerProps {
  entries: readonly MentionEntry[];
  rect: { left: number; bottom: number };
  placeholder: string;
  onPick: (entry: MentionEntry) => void;
  onClose: () => void;
}

// A filterable list of the book's Knowledge base entries, opened at a point on screen: the entry for a new Codex
// addition (from the `/` menu), or the one an addition card changes to.
export function EntryPicker({ entries, rect, placeholder, onPick, onClose }: EntryPickerProps) {
  useReturnFocus();
  const [query, setQuery] = useState('');
  const shown = filterEntries(entries, query);
  return (
    <div
      data-testid={writeTestIds.entryPicker}
      className="prose-popup"
      style={{ position: 'fixed', left: rect.left, top: rect.bottom + 4 }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <input
        autoFocus
        data-testid={writeTestIds.entryPickerSearch}
        aria-label={placeholder}
        placeholder={placeholder}
        className="w-full border-b border-border bg-transparent px-2 py-1 text-sm outline-none"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && shown[0]) onPick(shown[0]);
        }}
      />
      {shown.map((entry) => (
        <button
          key={entry.id}
          type="button"
          data-testid={writeTestIds.entryPickerOption(entry.id)}
          className="prose-popup-item w-full text-left"
          onClick={() => onPick(entry)}
        >
          <span>{entry.name}</span>
          <span className="prose-popup-meta">{entry.category}</span>
        </button>
      ))}
      {shown.length === 0 && <div className="prose-popup-empty">No matching entry</div>}
    </div>
  );
}
