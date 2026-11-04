import type { Outline } from '@codex/core/api/structure';
import { liveChapters } from '@codex/core/features/plan/format';
import type { Move, MoveKind } from '@codex/core/features/plan/moveOutline';

export interface Point {
  x: number;
  y: number;
}

/** The accent line a drag shows, in viewport pixels. */
export interface DropLineRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

/** What a droppable carries in its `data`. */
export interface DropOver {
  kind: MoveKind;
  chapterId?: string;
  actId?: string;
}

export interface DropTarget {
  move: Move;
  line: DropLineRect;
}

const rectOf = (prefix: string, id: string) =>
  document.getElementById(`${prefix}-${id}`)?.getBoundingClientRect() ?? null;

const present = (rects: (DOMRect | null)[]): DOMRect[] =>
  rects.filter((r): r is DOMRect => r !== null);

/** Where a dragged item would land with the pointer at `at` over the droppable `over`, and the line that shows it.
 *  The slot is read from the rendered rectangles, as the reference screen does. */
export function dropTarget(
  outline: Outline,
  kind: MoveKind,
  id: string,
  over: DropOver,
  at: Point,
): DropTarget | null {
  if (kind === 'scene' && over.chapterId !== undefined) {
    const chapter = outline.acts.flatMap(liveChapters).find((c) => c.id === over.chapterId);
    const column = rectOf('chapter', over.chapterId);
    if (!chapter || !column) return null;
    const cards = chapter.scenes.filter((s) => s.id !== id);
    const rects = present(cards.map((s) => rectOf('scene', s.id)));
    const slot = rects.filter((r) => at.y > r.top + r.height / 2).length;
    const top =
      slot < rects.length
        ? rects[slot].top - 4
        : rects.length > 0
          ? rects[rects.length - 1].bottom + 2
          : column.top + 48;
    return {
      move: { kind, id, chapterId: chapter.id, afterId: slot > 0 ? cards[slot - 1].id : null },
      line: { left: column.left + 6, top, width: column.width - 12, height: 2 },
    };
  }
  if (kind === 'chapter' && over.actId !== undefined) {
    const act = outline.acts.find((a) => a.id === over.actId);
    const row = rectOf('chapters', over.actId);
    if (!act || !row) return null;
    const columns = liveChapters(act).filter((c) => c.id !== id);
    const rects = present(columns.map((c) => rectOf('chapter', c.id)));
    const slot = rects.filter(
      (r) => at.y > r.bottom + 5 || (at.y >= r.top - 5 && at.x > r.left + r.width / 2),
    ).length;
    const beside = rects[Math.min(slot, rects.length - 1)] ?? row;
    return {
      move: { kind, id, actId: act.id, afterId: slot > 0 ? columns[slot - 1].id : null },
      line: {
        left: slot < rects.length ? beside.left - 6 : beside.right + 4,
        top: beside.top,
        width: 2,
        height: Math.max(60, beside.height),
      },
    };
  }
  if (kind === 'act') {
    const acts = outline.acts.filter((a) => a.id !== id);
    const rects = present(acts.map((a) => rectOf('act', a.id)));
    if (rects.length === 0) return null;
    const slot = rects.filter((r) => at.y > r.top + r.height / 2).length;
    const edge = slot < rects.length ? rects[slot].top : rects[rects.length - 1].bottom;
    const first = rects[0];
    return {
      move: { kind, id, afterId: slot > 0 ? acts[slot - 1].id : null },
      line: { left: first.left, top: edge - 1, width: first.width, height: 3 },
    };
  }
  return null;
}
