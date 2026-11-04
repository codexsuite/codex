import { useState } from 'react';
import { writeTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

export interface ChapterChoice {
  id: string;
  title: string;
  label: string;
}

interface ChapterPickerProps {
  chapters: readonly ChapterChoice[];
  rect: { left: number; bottom: number };
  onPick: (chapter: ChapterChoice) => void;
  onClose: () => void;
}

// "Chapter reference": a filterable list of the book's chapters, opened at the caret from the `/` menu.
export function ChapterPicker({ chapters, rect, onPick, onClose }: ChapterPickerProps) {
  useReturnFocus();
  const [query, setQuery] = useState('');
  const q = query.trim().toLowerCase();
  const shown = chapters.filter(
    (c) => q === '' || `${c.label} ${c.title}`.toLowerCase().includes(q),
  );
  return (
    <div
      data-testid={writeTestIds.chapterPicker}
      className="prose-popup"
      style={{ position: 'fixed', left: rect.left, top: rect.bottom + 4 }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onClose();
      }}
    >
      <input
        autoFocus
        aria-label="Link to chapter"
        placeholder="Link to chapter…"
        className="w-full border-b border-border bg-transparent px-2 py-1 text-sm outline-none"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && shown[0]) onPick(shown[0]);
        }}
      />
      {shown.map((chapter) => (
        <button
          key={chapter.id}
          type="button"
          data-testid={writeTestIds.chapterPickerOption(chapter.id)}
          className="prose-popup-item w-full text-left"
          onClick={() => onPick(chapter)}
        >
          <span>{chapter.title}</span>
          <span className="prose-popup-meta">{chapter.label}</span>
        </button>
      ))}
      {shown.length === 0 && <div className="prose-popup-empty">No matching chapter</div>}
    </div>
  );
}
