import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { KeyboardEvent } from 'react';
import type { LibraryBook } from '@codex/core/api/types';
import { StatusDot } from '../../shell/StatusDot';
import { Cover } from '../library/Cover';
import { paddedNumber } from '@codex/core/features/series/seriesOrder';
import { seriesTestIds } from './testIds';

interface OrderRowProps {
  book: LibraryBook;
  /** 0-based place in the list; the book's number is index + 1. */
  index: number;
  last: boolean;
  gripRef: (node: HTMLButtonElement | null) => void;
  onMove: (delta: number) => void;
}

// One book in the settings order list: grip (drag, or Alt+Up / Alt+Down), live number, mini cover, title, arrows.
export function OrderRow({ book, index, last, gripRef, onMove }: OrderRowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: book.id });

  const onGripKeyDown = (event: KeyboardEvent) => {
    if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
    event.preventDefault();
    onMove(event.key === 'ArrowUp' ? -1 : 1);
  };
  const setGrip = (node: HTMLButtonElement | null) => {
    setActivatorNodeRef(node);
    gripRef(node);
  };

  return (
    <li
      ref={setNodeRef}
      data-testid={seriesTestIds.orderRow(book.id)}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={`flex items-center gap-2 rounded border border-border bg-surface px-2 py-1 ${isDragging ? 'z-10 shadow-lg' : ''}`}
    >
      <button
        type="button"
        ref={setGrip}
        data-testid={seriesTestIds.grip(book.id)}
        title="Drag to reorder"
        aria-label={`Move ${book.title} (Alt+Up or Alt+Down)`}
        {...attributes}
        {...listeners}
        onKeyDown={onGripKeyDown}
        className="cursor-grab touch-none border-0 bg-transparent px-1 text-muted"
      >
        ⠿
      </button>
      <span
        data-testid={seriesTestIds.orderNumber(book.id)}
        className="w-8 text-xs text-muted tabular-nums"
      >
        {paddedNumber(index + 1)}
      </span>
      <div style={{ ['--cover-width' as string]: '22px' }}>
        <Cover palette={book.coverPalette} imageUrl={book.coverUrl} title="" />
      </div>
      <span className="min-w-0 flex-1 truncate text-sm font-medium">{book.title}</span>
      <StatusDot status={book.status} />
      <button
        type="button"
        data-testid={seriesTestIds.moveUp(book.id)}
        disabled={index === 0}
        aria-label={`Move ${book.title} up`}
        onClick={() => onMove(-1)}
        className="border-0 bg-transparent px-1 text-muted disabled:opacity-30"
      >
        ↑
      </button>
      <button
        type="button"
        data-testid={seriesTestIds.moveDown(book.id)}
        disabled={last}
        aria-label={`Move ${book.title} down`}
        onClick={() => onMove(1)}
        className="border-0 bg-transparent px-1 text-muted disabled:opacity-30"
      >
        ↓
      </button>
    </li>
  );
}
