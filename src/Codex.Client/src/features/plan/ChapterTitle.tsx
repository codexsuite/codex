import { useEffect, useRef, useState } from 'react';
import type { OutlineChapter } from '@codex/core/api/structure';
import { planTestIds } from './testIds';
import { useChapterOps } from './useChapterOps';

// A chapter's title. Double-click it, or ⋯ → Rename chapter, to edit it in place: Enter saves, Esc cancels, and an
// empty title is refused with a line under the field (the title reverts when you leave it).
export function ChapterTitle({
  chapter,
  editing,
  onDone,
  onEdit,
}: {
  chapter: OutlineChapter;
  editing: boolean;
  onDone: () => void;
  onEdit: () => void;
}) {
  if (!editing) {
    return (
      <span
        data-testid={planTestIds.chapterTitle(chapter.id)}
        onDoubleClick={onEdit}
        className="block text-md leading-[1.3] font-semibold [overflow-wrap:anywhere] text-text"
      >
        {chapter.title}
      </span>
    );
  }
  return <ChapterTitleEditor chapter={chapter} onDone={onDone} />;
}

function ChapterTitleEditor({ chapter, onDone }: { chapter: OutlineChapter; onDone: () => void }) {
  const { rename } = useChapterOps();
  const [value, setValue] = useState(chapter.title);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    input.current?.focus();
    input.current?.select();
  }, []);

  const save = async () => {
    const title = value.trim();
    if (title === '') {
      setError('The title cannot be empty.');
      return;
    }
    if (title === chapter.title) {
      onDone();
      return;
    }
    try {
      await rename(chapter.id, title);
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That title could not be saved.');
    }
  };

  return (
    <div>
      <input
        ref={input}
        data-testid={planTestIds.chapterTitleInput(chapter.id)}
        aria-label={`Title of ${chapter.title}`}
        value={value}
        onChange={(event) => {
          setValue(event.target.value);
          setError(null);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') void save();
          if (event.key === 'Escape') onDone();
        }}
        onBlur={() => (value.trim() === '' ? onDone() : void save())}
        className="w-full rounded-xs border border-border bg-bg px-1 text-md font-semibold text-text focus-visible:outline-2 focus-visible:outline-accent"
      />
      {error !== null && (
        <span
          role="alert"
          data-testid={planTestIds.chapterTitleError(chapter.id)}
          className="block text-xs text-danger"
        >
          {error}
        </span>
      )}
    </div>
  );
}
