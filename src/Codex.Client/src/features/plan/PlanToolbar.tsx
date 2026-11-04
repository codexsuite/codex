import type { Outline } from '@codex/core/api/structure';
import { ArchivedScenes } from './ArchivedScenes';
import { BoardIcon, OutlineIcon, PlusIcon, SearchIcon } from './icons';
import { LabelFilter } from './LabelFilter';
import type { PlanFilter } from '@codex/core/features/plan/planFilter';
import type { PlanView } from './planPrefs';
import { planTestIds } from './testIds';
import { toolbarButton, toolbarPrimary } from './toolbarStyles';
import { useCanWrite } from '../../shell/bookAccess';

const options: { view: PlanView; label: string; id: string; icon: () => React.JSX.Element }[] = [
  { view: 'board', label: 'Board', id: planTestIds.viewBoard, icon: BoardIcon },
  { view: 'outline', label: 'Outline', id: planTestIds.viewOutline, icon: OutlineIcon },
];

interface PlanToolbarProps {
  view: PlanView;
  onChange: (v: PlanView) => void;
  outline: Outline | undefined;
  filter: PlanFilter;
  onFilter: (filter: PlanFilter) => void;
  /** Every act is collapsed: the button then offers Expand all. */
  allCollapsed: boolean;
  onToggleAll: () => void;
  onNewAct: () => void;
  /** `#archived=<chapter>`: open the Archived panel at that chapter. */
  archivedChapterId?: string;
}

// `+ New Act` and the Board / Outline control, then on the right Find in plan, the Label filter, Archived and
// Collapse all.
export function PlanToolbar({
  view,
  onChange,
  outline,
  filter,
  onFilter,
  allCollapsed,
  onToggleAll,
  onNewAct,
  archivedChapterId,
}: PlanToolbarProps) {
  // Below write (#80) there is no New Act.
  const canWrite = useCanWrite();
  return (
    <div
      data-testid={planTestIds.toolbar}
      className="flex flex-wrap items-center gap-x-2 gap-y-1.5 border-b border-border px-3 py-2"
    >
      {canWrite && (
        <button
          type="button"
          data-testid={planTestIds.newAct}
          onClick={onNewAct}
          className={toolbarPrimary}
        >
          <PlusIcon />
          New Act
        </button>
      )}
      <div
        role="group"
        aria-label="View as"
        className="inline-flex h-[var(--control-height)] overflow-hidden rounded-sm border border-border bg-surface"
      >
        {options.map((o) => (
          <button
            key={o.view}
            type="button"
            data-testid={o.id}
            aria-pressed={view === o.view}
            onClick={() => onChange(o.view)}
            className="inline-flex cursor-pointer items-center gap-1 border-0 border-r border-border bg-transparent px-[9px] text-sm text-muted last:border-r-0 hover:text-text focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent aria-pressed:bg-accent-soft aria-pressed:font-semibold aria-pressed:text-text [&_svg]:size-[13px]"
          >
            <o.icon />
            {o.label}
          </button>
        ))}
      </div>
      <span className="flex-1" />
      <label className="flex h-[var(--control-height)] w-[200px] max-w-full items-center gap-1.5 rounded-sm border border-border bg-surface px-2 focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent max-sm:w-auto max-sm:flex-1">
        <span className="text-muted [&_svg]:size-[13px]">
          <SearchIcon />
        </span>
        <input
          type="search"
          data-testid={planTestIds.search}
          aria-label="Find scenes"
          placeholder="Find in plan…"
          value={filter.query}
          onChange={(event) => onFilter({ ...filter, query: event.target.value })}
          className="min-w-0 flex-1 border-0 bg-transparent text-sm text-text outline-none placeholder:text-muted"
        />
      </label>
      <LabelFilter
        outline={outline}
        labelId={filter.labelId}
        onChange={(labelId) => onFilter({ ...filter, labelId })}
      />
      <ArchivedScenes
        key={archivedChapterId}
        outline={outline}
        focusChapterId={archivedChapterId}
      />
      {view === 'board' && (
        <button
          type="button"
          data-testid={planTestIds.toggleAll}
          onClick={onToggleAll}
          className={toolbarButton}
        >
          {allCollapsed ? 'Expand all' : 'Collapse all'}
        </button>
      )}
    </div>
  );
}
