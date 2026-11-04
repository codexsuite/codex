import { useEffect, useState } from 'react';
import type { Outline } from '@codex/core/api/structure';
import { ArchivedChapter } from './ArchivedChapter';
import { ArchiveIcon } from './icons';
import { planTestIds } from './testIds';
import { ToolbarPopover } from './ToolbarPopover';
import { toolbarButton } from './toolbarStyles';

// `Archived (n)` in the toolbar: the book's archived scenes, grouped by chapter, each with Restore.
export function ArchivedScenes({
  outline,
  focusChapterId,
}: {
  outline: Outline | undefined;
  /** `#archived=<chapter>`: open the panel at that chapter (a link to an archived chapter lands here). */
  focusChapterId?: string;
}) {
  const [picked, setPicked] = useState<boolean | null>(null);
  const [dismissedFor, setDismissedFor] = useState<string | null>(null);
  // A link target opens the panel until it is closed once; after that the toolbar button decides.
  const open = picked ?? (focusChapterId !== undefined && dismissedFor !== focusChapterId);
  const setOpen = (next: boolean) => {
    setPicked(next);
    if (!next) setDismissedFor(focusChapterId ?? null);
  };
  const chapters = (outline?.acts ?? [])
    .flatMap((act) => act.chapters)
    .filter((chapter) => chapter.archived || chapter.archivedSceneCount > 0);
  const shown = focusChapterId !== undefined && chapters.some((c) => c.id === focusChapterId);
  useEffect(() => {
    if (focusChapterId === undefined) return;
    document
      .getElementById(`archived-chapter-${focusChapterId}`)
      ?.scrollIntoView?.({ block: 'center' });
  }, [focusChapterId, shown, open]);
  const total = chapters.reduce((sum, chapter) => sum + chapter.archivedSceneCount, 0);
  return (
    <ToolbarPopover
      open={open}
      onClose={() => setOpen(false)}
      testId={planTestIds.archivedList}
      wide
      trigger={
        <button
          type="button"
          data-testid={planTestIds.archivedToggle}
          aria-haspopup="dialog"
          aria-expanded={open}
          title="Chapters and scenes taken off the board; restore them from here"
          onClick={() => setOpen(!open)}
          className={`${toolbarButton} ${total === 0 ? '!border-transparent !bg-transparent hover:!bg-surface-2' : ''}`}
        >
          <ArchiveIcon />
          Archived ({total})
        </button>
      }
    >
      <div className="px-3 pt-2 pb-1 text-2xs font-semibold tracking-[.04em] text-muted uppercase">
        Archived chapters and scenes
      </div>
      <div className="flex flex-col overflow-auto px-1 pb-1">
        {chapters.length === 0 && (
          <p className="m-0 px-3 py-4 text-center text-sm text-muted">
            Nothing archived. Card ⋯ → <b>Archive scene</b> or chapter ⋯ → <b>Delete chapter</b>{' '}
            takes it off the board without deleting it.
          </p>
        )}
        {chapters.map((chapter) => (
          <ArchivedChapter
            key={chapter.id}
            chapter={chapter}
            focused={chapter.id === focusChapterId}
          />
        ))}
      </div>
    </ToolbarPopover>
  );
}
