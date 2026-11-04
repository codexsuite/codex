import { useState } from 'react';
import type { OutlineChapter } from '@codex/core/api/structure';
import { MoreIcon } from './icons';
import { planTestIds } from './testIds';
import { ToolbarPopover } from './ToolbarPopover';
import { useChapterOps } from './useChapterOps';

const itemClass =
  'block w-full cursor-pointer rounded-sm border-0 bg-transparent px-2 py-1 text-left text-sm text-text hover:bg-accent-soft';

// The chapter header's ⋯ (shown on hover or focus, as in design/screens/plan.html): Rename chapter, Opener art…, then
// Delete chapter (which archives it: Undo in the toast, Restore in Archived).
export function ChapterMenu({
  chapter,
  name,
  onRename,
  onOpener,
}: {
  chapter: OutlineChapter;
  /** "Chapter 3: Classes", for the button's label. */
  name: string;
  onRename: () => void;
  onOpener: () => void;
}) {
  const [open, setOpen] = useState(false);
  const { archive } = useChapterOps();
  return (
    <ToolbarPopover
      open={open}
      onClose={() => setOpen(false)}
      testId={planTestIds.chapterMenu(chapter.id)}
      align="left"
      trigger={
        <button
          type="button"
          data-testid={planTestIds.chapterMore(chapter.id)}
          aria-label={`${name} actions`}
          aria-haspopup="menu"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
          className="inline-grid size-[22px] flex-none cursor-pointer place-items-center rounded-xs border-0 bg-transparent p-0 text-muted opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 hover:bg-surface hover:text-text aria-expanded:opacity-100 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-[13px]"
        >
          <MoreIcon />
        </button>
      }
    >
      <div role="menu" className="p-1">
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.chapterRename(chapter.id)}
          onClick={() => {
            setOpen(false);
            onRename();
          }}
          className={itemClass}
        >
          Rename chapter
        </button>
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.chapterOpener(chapter.id)}
          onClick={() => {
            setOpen(false);
            onOpener();
          }}
          className={itemClass}
        >
          Opener art…
        </button>
        <div className="my-1 h-px bg-border" />
        <button
          type="button"
          role="menuitem"
          data-testid={planTestIds.chapterDelete(chapter.id)}
          onClick={() => {
            setOpen(false);
            void archive(chapter.id);
          }}
          className={`${itemClass} !text-danger`}
        >
          Delete chapter
        </button>
      </div>
    </ToolbarPopover>
  );
}
