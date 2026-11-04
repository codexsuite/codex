import type { Node as PMNode } from '@tiptap/pm/model';

export type LocateKind = 'beat' | 'kbAddition' | 'ref' | 'mention' | 'chapterLink';

export interface LocateTarget {
  kind: LocateKind;
  id: string;
}

/** A document range: `from`/`to` are ProseMirror positions, usable with `setTextSelection` or `scrollIntoView`. */
export interface Position {
  from: number;
  to: number;
}

/** Every place a beat, Codex addition, source marker, mention or chapter link with this id appears, in document order. */
export function locate(doc: PMNode, target: LocateTarget): Position[] {
  const found: Position[] = [];
  doc.descendants((node, pos) => {
    if (matchesNode(node, target)) {
      found.push({ from: pos, to: pos + node.nodeSize });
    } else if (
      node.isText &&
      node.marks.some((m) => matchesMark(m.type.name, m.attrs.id as string, target))
    ) {
      const last = found[found.length - 1];
      if (last && last.to === pos && target.kind !== 'mention') {
        last.to = pos + node.nodeSize;
      } else {
        found.push({ from: pos, to: pos + node.nodeSize });
      }
    }
    return true;
  });
  return found;
}

function matchesNode(node: PMNode, target: LocateTarget): boolean {
  switch (target.kind) {
    case 'beat':
      return (
        node.type.name === 'directive' && node.attrs.name === 'beat' && node.attrs.ref === target.id
      );
    case 'kbAddition':
      return (
        node.type.name === 'directive' &&
        node.attrs.name === 'kb-addition' &&
        node.attrs.ref === target.id
      );
    case 'mention':
      return node.type.name === 'mention' && node.attrs.id === target.id;
    case 'ref':
      return node.type.name === 'refMarker' && node.attrs.id === target.id;
    default:
      return false;
  }
}

function matchesMark(name: string, id: string, target: LocateTarget): boolean {
  return (
    (target.kind === 'ref' && name === 'ref' && id === target.id) ||
    (target.kind === 'chapterLink' && name === 'chapterLink' && id === target.id)
  );
}
