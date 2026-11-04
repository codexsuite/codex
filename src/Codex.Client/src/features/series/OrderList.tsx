import { DndContext, PointerSensor, closestCenter, useSensor, useSensors } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, arrayMove, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { useLayoutEffect, useRef } from 'react';
import type { LibraryBook } from '@codex/core/api/types';
import { OrderRow } from './OrderRow';
import { moveBy } from '@codex/core/features/series/seriesOrder';
import { seriesTestIds } from './testIds';

interface OrderListProps {
  books: readonly LibraryBook[];
  /** The ids in their new order. */
  onChange: (ids: string[]) => void;
}

// The books of a series in order. Grip drag and Alt+Up/Down both report the new id order; the moved book's
// grip keeps focus afterwards.
export function OrderList({ books, onChange }: OrderListProps) {
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));
  const grips = useRef(new Map<string, HTMLButtonElement>());
  const refocus = useRef<string | null>(null);
  const ids = books.map((b) => b.id);

  useLayoutEffect(() => {
    if (refocus.current !== null) grips.current.get(refocus.current)?.focus();
    refocus.current = null;
  });

  const move = (index: number, delta: number) => {
    const next = moveBy(ids, index, delta);
    if (next === ids) return;
    refocus.current = ids[index];
    onChange([...next]);
  };

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return;
    const from = ids.indexOf(String(active.id));
    const to = ids.indexOf(String(over.id));
    refocus.current = String(active.id);
    onChange(arrayMove(ids, from, to));
  };

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <ol
          data-testid={seriesTestIds.orderList}
          aria-label="Book order"
          className="m-0 flex list-none flex-col gap-1 p-0"
        >
          {books.map((book, index) => (
            <OrderRow
              key={book.id}
              book={book}
              index={index}
              last={index === books.length - 1}
              gripRef={(node) => {
                if (node) grips.current.set(book.id, node);
                else grips.current.delete(book.id);
              }}
              onMove={(delta) => move(index, delta)}
            />
          ))}
        </ol>
      </SortableContext>
    </DndContext>
  );
}
