import { useState } from 'react';
import type { MentionEntry } from '../../prose';
import { filterEntries } from '../../prose/entries';
import { writeTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface BeatContextPickerProps {
  entries: readonly MentionEntry[];
  /** Entries already sent as context; they are not offered again. */
  chosen: readonly string[];
  onPick: (entry: MentionEntry) => void;
  onClose: () => void;
}

// "+ Context": pick a Knowledge base entry to send to the model with this beat.
export function BeatContextPicker({ entries, chosen, onPick, onClose }: BeatContextPickerProps) {
  useReturnFocus();
  const [query, setQuery] = useState('');
  const shown = filterEntries(
    entries.filter((e) => !chosen.includes(e.id)),
    query,
  );
  return (
    <div
      data-testid={writeTestIds.beatContextPicker}
      className="prose-popup"
      style={{ position: 'absolute', zIndex: 20 }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <input
        autoFocus
        data-testid={writeTestIds.beatContextSearch}
        aria-label="Find an entry"
        placeholder="Find an entry…"
        className="w-full border-b border-border bg-transparent px-2 py-1 text-sm outline-none"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      {shown.map((entry) => (
        <button
          key={entry.id}
          type="button"
          data-testid={writeTestIds.beatContextOption(entry.id)}
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
