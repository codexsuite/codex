import { ChevronDownIcon, ChevronLeftIcon, ChevronRightIcon } from './icons';
import type { ChapterView } from '@codex/core/features/write/manuscriptView';
import { writeTestIds } from './testIds';

interface ChapterJumpProps {
  chapters: readonly ChapterView[];
  /** The chapter being read; the menu shows it, and ‹ › step from it. */
  current: string | null;
  onJump: (chapterId: string) => void;
}

const label = ({ chapter, overline }: ChapterView) =>
  overline ? `${overline}: ${chapter.title}` : chapter.title;

// The toolbar's chapter menu between ‹ and ›: it reads as the chapter in view, and picking one (or stepping)
// scrolls the manuscript to it.
export function ChapterJump({ chapters, current, onJump }: ChapterJumpProps) {
  const at = chapters.findIndex((c) => c.chapter.id === current);
  const step = (by: -1 | 1) => {
    const target = chapters[at + by];
    if (target) onJump(target.chapter.id);
  };
  return (
    <div className="inline-flex min-w-0 items-center gap-0.5">
      <button
        type="button"
        data-testid={writeTestIds.chapterPrevious}
        aria-label="Previous chapter"
        title="Previous chapter"
        disabled={at <= 0}
        onClick={() => step(-1)}
        className="write-ib"
      >
        <ChevronLeftIcon />
      </button>
      <span className="write-sel max-w-[300px]">
        <select
          data-testid={writeTestIds.chapterJump}
          aria-label="Jump to chapter"
          value={current ?? ''}
          onChange={(event) => {
            if (event.target.value !== '') onJump(event.target.value);
          }}
        >
          {current === null && <option value="">Chapter</option>}
          {chapters.map((view) => (
            <option key={view.chapter.id} value={view.chapter.id}>
              {label(view)}
            </option>
          ))}
        </select>
        <ChevronDownIcon />
      </span>
      <button
        type="button"
        data-testid={writeTestIds.chapterNext}
        aria-label="Next chapter"
        title="Next chapter"
        disabled={at < 0 || at >= chapters.length - 1}
        onClick={() => step(1)}
        className="write-ib"
      >
        <ChevronRightIcon />
      </button>
    </div>
  );
}
