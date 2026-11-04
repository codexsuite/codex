import { useDroppable } from '@dnd-kit/core';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import type { OutlineAct } from '@codex/core/api/structure';
import { ActMenu } from './ActMenu';
import { ActTitle } from './ActTitle';
import {
  actScenes,
  liveChapters,
  plural,
  sceneWords,
  wordsText,
} from '@codex/core/features/plan/format';
import { ChapterColumn } from './ChapterColumn';
import { OpenerDialog } from './OpenerDialog';
import { OpenerThumb } from './OpenerThumb';
import { DragGrip } from './DragGrip';
import { ChevronDownIcon, PenIcon, PlusIcon } from './icons';
import type { PlanFilter } from '@codex/core/features/plan/planFilter';
import { planTestIds } from './testIds';
import { useCreate } from './useCreate';
import { usePlanEdit } from './usePlanEdit';
import { useCanWrite } from '../../shell/bookAccess';

// One segment of the mockup's `.split`: the segments share the outer border and are divided by a rule.
const segment =
  'inline-flex h-full cursor-pointer items-center gap-[5px] border-0 border-r border-border bg-transparent px-2 text-sm font-medium text-text hover:bg-surface-2 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent [&_svg]:size-[13px]';

interface ActSectionProps {
  act: OutlineAct;
  index: number;
  collapsed: boolean;
  onToggle: () => void;
  highlightedEntry: string | null;
  filter: PlanFilter;
  /** The book bar's scope is this chapter: the act shows only it, with no `+ New chapter` slot. */
  onlyChapterId?: string | null;
}

// An act: a title row with the collapse toggle and the title (edited in place), a row with the split button
// (`+ New Chapter | ✎ | ⋯`) and the counts, then its chapter columns (indented under the title), which wrap onto
// several rows and end with a dashed `+ New chapter` slot.
export function ActSection({
  act,
  index,
  collapsed,
  onToggle,
  highlightedEntry,
  filter,
  onlyChapterId = null,
}: ActSectionProps) {
  const { bookId = '' } = useParams();
  const [openerOpen, setOpenerOpen] = useState(false);
  const { createChapter } = useCreate();
  const { setFocusActId } = usePlanEdit();
  // Below write (#80): no New Chapter, rename or act menu, and the opener only shows.
  const canWrite = useCanWrite();
  const chapters = liveChapters(act);
  const shown = onlyChapterId === null ? chapters : chapters.filter((c) => c.id === onlyChapterId);
  const scenes = actScenes(act);
  const marker = `Act ${index + 1}${act.title.trim() === '' ? '' : ':'}`;
  const name = act.title.trim() === '' ? marker : `${marker} ${act.title}`;
  const { setNodeRef } = useDroppable({
    id: `chapters:${act.id}`,
    data: { kind: 'chapter', actId: act.id },
  });
  const addChapter = () => {
    if (collapsed) onToggle();
    void createChapter(act.id);
  };
  return (
    <section
      id={`act-${act.id}`}
      data-testid={planTestIds.act(act.id)}
      data-collapsed={collapsed}
      className="border-border pt-2 pb-3 not-first:border-t"
    >
      <header className="group -ml-1 flex items-center gap-1">
        <DragGrip kind="act" id={act.id} label={name} />
        <button
          type="button"
          data-testid={planTestIds.actToggle(act.id)}
          aria-expanded={!collapsed}
          aria-label={`${collapsed ? 'Expand' : 'Collapse'} ${name}`}
          onClick={onToggle}
          className="inline-grid size-[22px] flex-none cursor-pointer place-items-center rounded-xs border-0 bg-transparent p-0 text-muted hover:bg-surface-2 hover:text-text focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-[13px] [&_svg]:transition-transform"
        >
          <span className={collapsed ? '-rotate-90' : ''}>
            <ChevronDownIcon />
          </span>
        </button>
        <ActTitle act={act} index={index} />
      </header>
      {openerOpen && (
        <OpenerDialog
          bookId={bookId}
          kind="act"
          id={act.id}
          name={name}
          opener={act.opener}
          onClose={() => setOpenerOpen(false)}
        />
      )}
      <div
        className={`ml-11 flex flex-wrap items-center gap-2 max-sm:ml-0 ${collapsed ? 'mt-1.5' : 'mt-1.5 mb-2.5'}`}
      >
        {canWrite && (
          <div className="inline-flex h-[var(--control-height)] rounded-sm border border-border bg-surface [&>*:last-child>button]:border-r-0">
            <button
              type="button"
              data-testid={planTestIds.newChapter(act.id)}
              onClick={addChapter}
              className={`${segment} rounded-l-sm`}
            >
              <PlusIcon />
              New Chapter
            </button>
            <button
              type="button"
              data-testid={planTestIds.actRename(act.id)}
              aria-label={`Rename ${name}`}
              title="Rename act"
              onClick={() => setFocusActId(act.id)}
              className={segment}
            >
              <PenIcon />
            </button>
            <ActMenu
              act={act}
              name={name}
              collapsed={collapsed}
              onToggle={onToggle}
              onOpener={() => setOpenerOpen(true)}
              className={`${segment} rounded-r-sm`}
            />
          </div>
        )}
        <OpenerThumb
          opener={act.opener}
          name={name}
          testId={planTestIds.actOpenerThumb(act.id)}
          onClick={canWrite ? () => setOpenerOpen(true) : undefined}
        />
        <span data-testid={planTestIds.actMeta(act.id)} className="text-xs text-muted">
          {chapters.length > 0
            ? `${plural(chapters.length, 'chapter')} · ${plural(scenes.length, 'scene')} · ${wordsText(sceneWords(scenes))}`
            : 'No chapters yet.'}
        </span>
      </div>
      {!collapsed && (
        <div
          ref={setNodeRef}
          id={`chapters-${act.id}`}
          className="flex min-h-8 flex-wrap items-start gap-2.5 pb-2 pl-11 max-sm:pl-0"
        >
          {shown.map((chapter) => (
            <ChapterColumn
              key={chapter.id}
              chapter={chapter}
              highlightedEntry={highlightedEntry}
              filter={filter}
            />
          ))}
          {canWrite && onlyChapterId === null && (
            <button
              type="button"
              data-testid={planTestIds.addChapter(act.id)}
              onClick={addChapter}
              className="grid min-h-[90px] w-[140px] flex-none cursor-pointer place-items-center rounded-md border border-dashed border-border bg-transparent text-sm text-muted hover:border-accent hover:text-accent"
            >
              + New chapter
            </button>
          )}
        </div>
      )}
    </section>
  );
}
