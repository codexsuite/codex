import type { Editor } from '@tiptap/react';
import type { Node as PMNode, ResolvedPos } from '@tiptap/pm/model';
import { selectedRect } from '@tiptap/pm/tables';

export type Align = 'left' | 'center' | 'right' | null;

export type TableOp =
  | 'addRowBefore'
  | 'addRowAfter'
  | 'addColumnBefore'
  | 'addColumnAfter'
  | 'deleteRow'
  | 'deleteColumn'
  | 'deleteTable';

/** Where the caret sits in a table: the table, its position, and the caret's row and column. Null outside one. */
export interface TableCaret {
  table: PMNode;
  pos: number;
  row: number;
  column: number;
}

export function tableCaret(editor: Editor): TableCaret | null {
  const $from: ResolvedPos = editor.state.selection.$from;
  for (let depth = $from.depth; depth > 0; depth--) {
    if ($from.node(depth).type.name === 'table') {
      return {
        table: $from.node(depth),
        pos: $from.before(depth),
        row: $from.index(depth),
        column: $from.index(depth + 1),
      };
    }
  }
  return null;
}

/** The table's alignments, one per column, whatever length the stored attribute has. */
function alignments(table: PMNode): Align[] {
  const stored = table.attrs.align as Align[];
  const columns = table.firstChild?.childCount ?? 0;
  return Array.from({ length: columns }, (_, c) => stored[c] ?? null);
}

// A table the editor has changed no longer matches the text it was read from.
function setAttrs(editor: Editor, pos: number, align: Align[]): void {
  const { tr, doc } = editor.state;
  const table = doc.nodeAt(pos);
  if (table)
    editor.view.dispatch(tr.setNodeMarkup(pos, undefined, { ...table.attrs, align, source: null }));
}

/**
 * Runs a toolbar operation on the table around the caret. The header row is the table's first row: nothing is added
 * above it, and it is not deleted while body rows remain (deleting the only row deletes the table).
 */
export function runTableOp(editor: Editor, op: TableOp): void {
  const caret = tableCaret(editor);
  if (!caret) return;
  const { table } = caret;
  // The operation works on the whole selection (a cell selection can span rows and columns), so the alignments
  // follow the selection's columns, not the caret's.
  const { top, left, right } = selectedRect(editor.state);
  const columns = table.firstChild?.childCount ?? 0;
  if (op === 'addRowBefore' && top === 0) return;
  if (op === 'deleteRow' && top === 0 && table.childCount > 1) return;
  if (op === 'deleteColumn' && left === 0 && right >= columns) {
    editor.chain().focus().deleteTable().run();
    return;
  }
  if (!editor.chain().focus()[op]().run()) return;
  if (op === 'deleteTable') return;

  const after = tableCaret(editor);
  if (!after) return;
  const align = alignments(table);
  if (op === 'addColumnBefore') align.splice(left, 0, null);
  if (op === 'addColumnAfter') align.splice(right, 0, null);
  if (op === 'deleteColumn') align.splice(left, right - left);
  setAttrs(editor, after.pos, align);
}

/** Sets the alignment of the caret's column; `null` goes back to the default. */
export function alignColumn(editor: Editor, side: Align): void {
  const caret = tableCaret(editor);
  if (!caret) return;
  const align = alignments(caret.table);
  align[caret.column] = side;
  setAttrs(editor, caret.pos, align);
}

/** A table of `rows` rows (the first a header row) by `columns` columns, empty, with no source text. */
export function newTable(rows: number, columns: number) {
  const cell = (type: 'tableHeader' | 'tableCell') => ({
    type,
    content: [{ type: 'paragraph' }],
  });
  return {
    type: 'table',
    attrs: { align: Array.from({ length: columns }, () => null), source: null },
    content: Array.from({ length: rows }, (_, r) => ({
      type: 'tableRow',
      content: Array.from({ length: columns }, () => cell(r === 0 ? 'tableHeader' : 'tableCell')),
    })),
  };
}
