import type { Outline } from '@codex/core/api/structure';
import { useDroppable } from '@dnd-kit/core';
import { ActSection } from './ActSection';
import { PlanDnd } from './PlanDnd';
import type { PlanFilter } from '@codex/core/features/plan/planFilter';
import { actInScope, type PlanScope } from '@codex/core/features/plan/planScope';
import { planTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface BoardProps {
  outline: Outline;
  isCollapsed: (actId: string, actIndex: number) => boolean;
  onToggleAct: (actId: string, collapsed: boolean) => void;
  highlightedEntry: string | null;
  filter: PlanFilter;
  /** The book bar's scope: acts and chapters outside it are not drawn (each act keeps its number). */
  scope: PlanScope;
  onAddAct: () => void;
}

// Acts as sections, in order, then a dashed `+ New act` (on the whole manuscript only). Acts, chapters and scenes
// reorder by drag (PlanDnd).
export function Board(props: BoardProps) {
  return (
    <PlanDnd outline={props.outline}>
      <Acts {...props} />
    </PlanDnd>
  );
}

function Acts({
  outline,
  isCollapsed,
  onToggleAct,
  highlightedEntry,
  filter,
  scope,
  onAddAct,
}: BoardProps) {
  const canWrite = useCanWrite();
  const { setNodeRef } = useDroppable({ id: 'board', data: { kind: 'act' } });
  return (
    <div ref={setNodeRef} data-testid={planTestIds.board}>
      {outline.acts.map((act, index) => {
        if (!actInScope(act, scope)) return null;
        const collapsed = isCollapsed(act.id, index);
        return (
          <ActSection
            key={act.id}
            act={act}
            index={index}
            collapsed={collapsed}
            onToggle={() => onToggleAct(act.id, !collapsed)}
            highlightedEntry={highlightedEntry}
            filter={filter}
            onlyChapterId={scope.kind === 'chapter' ? scope.id : null}
          />
        );
      })}
      {canWrite && scope.kind === 'book' && (
        <button
          type="button"
          data-testid={planTestIds.addAct}
          onClick={onAddAct}
          className="mt-2 h-[38px] w-full cursor-pointer rounded-md border border-dashed border-border bg-transparent text-sm text-muted hover:border-accent hover:text-accent"
        >
          + New act
        </button>
      )}
    </div>
  );
}
