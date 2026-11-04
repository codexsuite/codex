import { Fragment, type Node as PmNode } from '@tiptap/pm/model';
import { parse, serialize } from '../../prose/markdown';

// Pure edits of a scene body as top-level blocks. A "unit" is a beat line and the blocks under it (its prose), up to
// the next directive; moving a beat moves its unit. Bodies go to the server whole, never as block indices.

export const nodes = (doc: PmNode): PmNode[] => {
  const list: PmNode[] = [];
  doc.forEach((child) => list.push(child));
  return list;
};

export function bodyOf(blocks: readonly PmNode[], like: PmNode): string {
  const kept = blocks.filter((b) => !(b.type.name === 'paragraph' && b.content.size === 0));
  if (kept.length === 0) return '';
  return serialize(like.type.create(null, Fragment.fromArray(kept)));
}

const isDirective = (n: PmNode) => n.type.name === 'directive';
const isBeat = (n: PmNode, id?: string) =>
  isDirective(n) && n.attrs.name === 'beat' && (id === undefined || n.attrs.ref === id);

/** Block index ranges [start, end) of the beat units; free prose before the first beat is not a unit. */
function unitRange(blocks: readonly PmNode[], beatId: string): [number, number] | null {
  const start = blocks.findIndex((b) => isBeat(b, beatId));
  if (start < 0) return null;
  let end = start + 1;
  while (end < blocks.length && !isDirective(blocks[end]!)) end += 1;
  return [start, end];
}

/** Where each beat unit starts, plus 0: the places Alt+arrow steps between. */
function segmentStarts(blocks: readonly PmNode[]): number[] {
  const starts = blocks.flatMap((b, i) => (isBeat(b) ? [i] : []));
  return starts[0] === 0 ? starts : [0, ...starts];
}

export interface Extracted {
  /** The body without the unit. */
  rest: PmNode[];
  unit: PmNode[];
  /** Index in `rest` where the unit was. */
  at: number;
}

export function extractUnit(body: string, beatId: string): (Extracted & { like: PmNode }) | null {
  const doc = parse(body, 'prose');
  const blocks = nodes(doc);
  const range = unitRange(blocks, beatId);
  if (!range) return null;
  return {
    rest: [...blocks.slice(0, range[0]), ...blocks.slice(range[1])],
    unit: blocks.slice(range[0], range[1]),
    at: range[0],
    like: doc,
  };
}

/** `rest` with `unit` inserted before block `index` (clamped). */
export function withUnit(
  rest: readonly PmNode[],
  unit: readonly PmNode[],
  index: number,
  like: PmNode,
): string {
  const at = Math.max(0, Math.min(index, rest.length));
  return bodyOf([...rest.slice(0, at), ...unit, ...rest.slice(at)], like);
}

export type Step = { scene: 'same'; index: number } | { scene: 'previous' | 'next'; index: number };

/** Where Alt+Up / Alt+Down puts a beat: one unit over, or the end of the previous / start of the next scene. */
export function stepTarget(body: string, beatId: string, direction: 'up' | 'down'): Step {
  const extracted = extractUnit(body, beatId);
  if (!extracted) return { scene: 'same', index: 0 };
  const whole = nodes(parse(body, 'prose'));
  const starts = segmentStarts(whole);
  const here = extracted.at;
  if (direction === 'up') {
    const before = starts.filter((s) => s < here);
    // The index is in the body without the unit, where earlier blocks keep their numbers.
    return before.length === 0
      ? { scene: 'previous', index: Number.MAX_SAFE_INTEGER }
      : { scene: 'same', index: before[before.length - 1]! };
  }
  const after = starts.filter((s) => s > here);
  if (after.length === 0) return { scene: 'next', index: 0 };
  const nextEnd = after.length > 1 ? after[1]! : whole.length;
  return { scene: 'same', index: nextEnd - extracted.unit.length };
}

export interface SplitBodies {
  head: string;
  tail: string;
}

/** Splits after the block holding `blockIndex`, dropping that block from the head only when it is empty. */
export function splitBody(doc: PmNode, blockIndex: number): SplitBodies {
  const blocks = nodes(doc);
  return {
    head: bodyOf(blocks.slice(0, blockIndex + 1), doc),
    tail: bodyOf(blocks.slice(blockIndex + 1), doc),
  };
}
