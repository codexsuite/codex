import { useDraggable } from '@dnd-kit/core';
import { useContext, type KeyboardEvent } from 'react';
import type { Arrow, MoveKind } from '@codex/core/features/plan/moveOutline';
import { ReorderContext } from './reorderContext';
import { planTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

const arrows: string[] = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'];

// The ⠿ handle of an act, chapter or scene: drag it, or press Alt+arrow while it has focus.
export function DragGrip({ kind, id, label }: { kind: MoveKind; id: string; label: string }) {
  const { keyMove } = useContext(ReorderContext);
  const canWrite = useCanWrite();
  const { attributes, listeners, setNodeRef } = useDraggable({
    id: `${kind}:${id}`,
    data: { kind, label },
  });
  const onKeyDown = (event: KeyboardEvent) => {
    if (!event.altKey || !arrows.includes(event.key)) return;
    event.preventDefault();
    keyMove(kind, id, event.key as Arrow);
  };
  // Below write (#80) nothing moves: no grip, so no drag and no Alt+arrows.
  if (!canWrite) return null;
  return (
    <button
      {...attributes}
      {...listeners}
      ref={setNodeRef}
      type="button"
      id={`grip-${kind}-${id}`}
      data-testid={planTestIds.grip(kind, id)}
      aria-label={`Move ${label}`}
      title="Drag, or press Alt+arrow keys"
      onKeyDown={onKeyDown}
      className="inline-flex h-5 w-4 shrink-0 cursor-grab touch-none items-center justify-center rounded-sm border-0 bg-transparent p-0 text-muted opacity-0 group-hover:opacity-100 focus-visible:opacity-100 hover:bg-accent-soft"
    >
      <svg viewBox="0 0 10 14" fill="currentColor" className="h-3.5 w-2.5" aria-hidden="true">
        <circle cx="3" cy="3" r="1.2" />
        <circle cx="7" cy="3" r="1.2" />
        <circle cx="3" cy="7" r="1.2" />
        <circle cx="7" cy="7" r="1.2" />
        <circle cx="3" cy="11" r="1.2" />
        <circle cx="7" cy="11" r="1.2" />
      </svg>
    </button>
  );
}
