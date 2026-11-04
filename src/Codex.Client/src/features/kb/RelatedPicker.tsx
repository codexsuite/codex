import { useState } from 'react';
import type { MentionEntry } from '../../prose';
import { kbTestIds } from './testIds';

interface RelatedPickerProps {
  /** Entries that can still be linked: not this entry, not already related. */
  choices: MentionEntry[];
  onPick: (entry: MentionEntry) => void;
}

// A searchable list of entries grouped by category.
export function RelatedPicker({ choices, onPick }: RelatedPickerProps) {
  const [query, setQuery] = useState('');
  const needle = query.trim().toLowerCase();
  const shown = choices.filter((c) => c.name.toLowerCase().includes(needle));
  const categories = [...new Set(shown.map((c) => c.category))];

  return (
    <div
      data-testid={kbTestIds.relatedPicker}
      className="absolute right-0 z-30 mt-1 w-56 rounded border border-border bg-surface shadow-lg"
    >
      <input
        type="search"
        autoFocus
        data-testid={kbTestIds.relatedSearch}
        aria-label="Find an entry"
        placeholder="Find an entry"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        className="h-7 w-full border-0 border-b border-border bg-bg px-2 text-sm text-text"
      />
      <div className="max-h-56 overflow-auto">
        {shown.length === 0 && (
          <p className="m-0 px-3 py-2 text-xs text-muted">No matching entries</p>
        )}
        {categories.map((category) => (
          <div key={category}>
            <h5 className="m-0 px-3 pt-2 text-2xs font-medium text-muted uppercase">{category}</h5>
            {shown
              .filter((c) => c.category === category)
              .map((c) => (
                <button
                  key={c.id}
                  type="button"
                  data-testid={kbTestIds.relatedOption(c.id)}
                  onClick={() => onPick(c)}
                  className="flex h-[var(--row-height)] w-full items-center border-0 bg-transparent px-3 text-left text-sm text-text hover:bg-accent-soft"
                >
                  {c.name}
                </button>
              ))}
          </div>
        ))}
      </div>
    </div>
  );
}
