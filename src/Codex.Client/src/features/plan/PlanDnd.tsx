import {
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useParams } from 'react-router-dom';
import type { Outline } from '@codex/core/api/structure';
import { dropTarget, type DropLineRect, type DropOver, type DropTarget } from './dropTarget';
import {
  applyMove,
  stepMove,
  type Arrow,
  type MoveKind,
} from '@codex/core/features/plan/moveOutline';
import { ReorderContext } from './reorderContext';
import { planTestIds } from './testIds';
import { useReorder } from './useReorder';

// Only a droppable of the dragged kind can take the drop: scenes land in chapter columns, chapters in an act's row,
// acts on the board.
const collisionOf: CollisionDetection = (args) =>
  pointerWithin({
    ...args,
    droppableContainers: args.droppableContainers.filter(
      (c) => c.data.current?.kind === args.active.data.current?.kind,
    ),
  });

const orderOf = (outline: Outline) =>
  JSON.stringify(
    outline.acts.map((a) => [a.id, a.chapters.map((c) => [c.id, c.scenes.map((s) => s.id)])]),
  );

// Drag-and-drop and Alt+arrow reordering for the board: a drop line while dragging, optimistic moves that revert
// when the server refuses, and focus kept on the grip that moved.
export function PlanDnd({ outline, children }: { outline: Outline; children: ReactNode }) {
  const { bookId = '' } = useParams();
  const reorder = useReorder(bookId);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));
  const target = useRef<DropTarget | null>(null);
  const focusAfterMove = useRef<string | null>(null);
  const [line, setLine] = useState<DropLineRect | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);

  useEffect(() => {
    if (focusAfterMove.current === null) return;
    const grip = document.getElementById(focusAfterMove.current);
    if (grip) {
      grip.focus();
      focusAfterMove.current = null;
    }
  }, [outline]);

  const run = useCallback(
    (move: NonNullable<DropTarget['move']>) => {
      if (orderOf(applyMove(outline, move)) === orderOf(outline)) return;
      focusAfterMove.current = `grip-${move.kind}-${move.id}`;
      void reorder(move);
    },
    [outline, reorder],
  );

  const keyMove = useCallback(
    (kind: MoveKind, id: string, key: Arrow) => {
      const move = stepMove(outline, kind, id, key);
      if (move) run(move);
    },
    [outline, run],
  );
  const context = useMemo(() => ({ keyMove }), [keyMove]);

  const onDragStart = (event: DragStartEvent) => {
    setDragging(String(event.active.data.current?.label ?? ''));
  };
  const onDragMove = (event: DragMoveEvent) => {
    const { active, over, activatorEvent, delta } = event;
    const kind = active.data.current?.kind as MoveKind | undefined;
    const id = String(active.id).slice(String(active.id).indexOf(':') + 1);
    if (!kind || !over || !(activatorEvent instanceof MouseEvent)) {
      target.current = null;
      setLine(null);
      return;
    }
    target.current = dropTarget(outline, kind, id, over.data.current as DropOver, {
      x: activatorEvent.clientX + delta.x,
      y: activatorEvent.clientY + delta.y,
    });
    setLine(target.current?.line ?? null);
  };
  const onDragEnd = (event: DragEndEvent) => {
    const landing = event.over ? target.current : null;
    target.current = null;
    setLine(null);
    setDragging(null);
    if (landing) run(landing.move);
  };
  const onDragCancel = () => {
    target.current = null;
    setLine(null);
    setDragging(null);
  };

  return (
    <ReorderContext.Provider value={context}>
      <DndContext
        sensors={sensors}
        collisionDetection={collisionOf}
        onDragStart={onDragStart}
        onDragMove={onDragMove}
        onDragEnd={onDragEnd}
        onDragCancel={onDragCancel}
      >
        {children}
        <DragOverlay dropAnimation={null}>
          {dragging !== null && (
            <div className="rounded-md border border-accent bg-surface px-2 py-1 text-sm text-text shadow-raised">
              {dragging}
            </div>
          )}
        </DragOverlay>
        {line && (
          <div
            data-testid={planTestIds.dropLine}
            className="pointer-events-none fixed z-50 rounded-full bg-accent"
            style={line}
          />
        )}
      </DndContext>
    </ReorderContext.Provider>
  );
}
