import { useEffect, useState } from 'react';
import { testIds } from '../../testIds';
import { SearchIcon } from './icons';

const debounceMs = 250;

// The search box types freely and tells the page once typing pauses; the server does the matching.
export function SearchBox({ value, onChange }: { value: string; onChange: (q: string) => void }) {
  const [text, setText] = useState(value);
  const [seen, setSeen] = useState(value);
  // The URL changed from outside (Clear search, Back): show it.
  if (value !== seen) {
    setSeen(value);
    setText(value);
  }

  useEffect(() => {
    if (text === value) return;
    const timer = window.setTimeout(() => onChange(text), debounceMs);
    return () => window.clearTimeout(timer);
  }, [text, value, onChange]);

  return (
    <label className="flex h-[var(--control-height)] w-[220px] max-w-full items-center gap-1.5 rounded-sm border border-border bg-surface px-2 focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent">
      <span className="flex size-[13px] text-muted [&>svg]:size-full">
        <SearchIcon />
      </span>
      <input
        type="search"
        aria-label="Search books and series"
        placeholder="Search name or series…"
        data-testid={testIds.shell.searchBox}
        value={text}
        onChange={(event) => setText(event.target.value)}
        className="min-w-0 flex-1 border-0 bg-transparent text-sm text-text outline-none placeholder:text-muted"
      />
    </label>
  );
}
