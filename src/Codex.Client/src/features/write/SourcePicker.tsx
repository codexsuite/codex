import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getSources, sourcesKey, type SourceRow } from '@codex/core/api/referenceSources';
import { markerTone, statusLabel } from './markerTone';
import { writeTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface SourcePickerProps {
  bookId: string;
  rect: { left: number; bottom: number };
  onPick: (source: SourceRow) => void;
  /** New source…: References' Add source, coming back here to cite what was added. */
  onNewSource: () => void;
  onClose: () => void;
}

const toneOf = (status: string) => (status === 'not_cited' ? 'muted' : markerTone(status));
const labelOf = (status: string) => (status === 'not_cited' ? 'Not cited' : statusLabel(status));

// `/` → Source reference: the book's sources with a dot in their clearance tone, filtered by title, then
// New source… (References' Add source).
export function SourcePicker({ bookId, rect, onPick, onNewSource, onClose }: SourcePickerProps) {
  useReturnFocus();
  const [query, setQuery] = useState('');
  const sources = useQuery({
    queryKey: sourcesKey(bookId),
    queryFn: ({ signal }) => getSources(bookId, { signal }),
  });
  const q = query.trim().toLowerCase();
  const shown = (sources.data ?? []).filter((s) => q === '' || s.title.toLowerCase().includes(q));
  return (
    <div
      data-testid={writeTestIds.citePicker}
      className="prose-popup write-cite-picker"
      style={{ position: 'fixed', left: rect.left, top: rect.bottom + 4 }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <input
        autoFocus
        data-testid={writeTestIds.citeSearch}
        aria-label="Find a source"
        placeholder="Find a source…"
        className="w-full border-b border-border bg-transparent px-2 py-1 text-sm outline-none"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && shown[0]) onPick(shown[0]);
        }}
      />
      {shown.map((source) => (
        <button
          key={source.id}
          type="button"
          data-testid={writeTestIds.citeOption(source.id)}
          data-tone={toneOf(source.status)}
          className="prose-popup-item write-cite-option w-full text-left"
          onClick={() => onPick(source)}
        >
          <i aria-label={labelOf(source.status)} role="img" />
          <span className="write-cite-title">{source.title}</span>
        </button>
      ))}
      {sources.isSuccess && shown.length === 0 && (
        <div className="prose-popup-empty">No matching source</div>
      )}
      <div className="write-cite-separator" />
      <button
        type="button"
        onClick={onNewSource}
        data-testid={writeTestIds.citeNewSource}
        className="prose-popup-item w-full text-left"
      >
        <span>+ New source…</span>
        <span className="prose-popup-meta">References</span>
      </button>
    </div>
  );
}
