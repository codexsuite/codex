import { Editor } from '@tiptap/react';
import { TextSelection } from '@tiptap/pm/state';
import { CellSelection, TableMap } from '@tiptap/pm/tables';
import { afterEach, describe, expect, it } from 'vitest';
import { proseExtensions } from '../schema/extensions';
import { proseSchema } from '../schema/schemas';
import { runTableOp, type Align, type TableOp } from './tableCommands';

let editor: Editor | null = null;
afterEach(() => editor?.destroy());

// A table of `rows` rows by 3 columns whose columns are aligned left, centre, right, with the cell texts "r<row>c<column>".
function tableEditor(rows = 3): Editor {
  const { nodes } = proseSchema;
  const cell = (r: number, c: number) =>
    nodes[r === 0 ? 'tableHeader' : 'tableCell'].create(
      null,
      nodes.paragraph.create(null, proseSchema.text(`r${r}c${c}`)),
    );
  const table = nodes.table.create(
    { align: ['left', 'center', 'right'], source: null },
    Array.from({ length: rows }, (_, r) =>
      nodes.tableRow.create(
        null,
        [0, 1, 2].map((c) => cell(r, c)),
      ),
    ),
  );
  editor = new Editor({
    element: document.createElement('div'),
    extensions: proseExtensions(),
    content: proseSchema.topNodeType.create(null, table).toJSON(),
  });
  return editor;
}

function cellPos(e: Editor, row: number, column: number): number {
  let tablePos = 0;
  e.state.doc.descendants((node, pos) => {
    if (node.type.name === 'table') tablePos = pos;
    return node.type.name !== 'table';
  });
  const table = e.state.doc.nodeAt(tablePos)!;
  return tablePos + 1 + TableMap.get(table).map[row * TableMap.get(table).width + column];
}

function select(e: Editor, from: [number, number], to: [number, number] = from) {
  const $anchor = e.state.doc.resolve(cellPos(e, ...from));
  const $head = e.state.doc.resolve(cellPos(e, ...to));
  e.view.dispatch(e.state.tr.setSelection(new CellSelection($anchor, $head)));
}

function tableOf(e: Editor) {
  let found = null as ReturnType<typeof e.state.doc.nodeAt>;
  e.state.doc.forEach((node) => {
    if (node.type.name === 'table') found = node;
  });
  return found!;
}

const headerTexts = (e: Editor) => {
  const out: string[] = [];
  tableOf(e).firstChild!.forEach((cell) => out.push(cell.textContent));
  return out;
};
const align = (e: Editor) => tableOf(e).attrs.align as Align[];

describe('table toolbar operations on a cell selection', () => {
  it.each<[TableOp, [number, number], [number, number], string[], Align[]]>([
    ['deleteColumn', [1, 0], [1, 1], ['r0c2'], ['right']],
    ['deleteColumn', [1, 1], [1, 2], ['r0c0'], ['left']],
    [
      'addColumnBefore',
      [1, 1],
      [1, 2],
      ['r0c0', '', 'r0c1', 'r0c2'],
      ['left', null, 'center', 'right'],
    ],
    [
      'addColumnAfter',
      [1, 0],
      [1, 1],
      ['r0c0', 'r0c1', '', 'r0c2'],
      ['left', 'center', null, 'right'],
    ],
  ])(
    'RunTableOp_%s_WhenSelectionSpansColumns_ShouldKeepAlignmentInStep %j %j',
    (op, from, to, texts, aligns) => {
      const e = tableEditor();
      select(e, from, to);

      runTableOp(e, op);

      expect(headerTexts(e)).toEqual(texts);
      expect(align(e)).toEqual(aligns);
    },
  );

  it('RunTableOp_WhenAddRowAfterOnHeaderRow_ShouldCreateBodyCells', () => {
    const e = tableEditor();
    select(e, [0, 1]);

    runTableOp(e, 'addRowAfter');

    const table = tableOf(e);
    expect(table.childCount).toBe(4);
    table.child(1).forEach((cell) => expect(cell.type.name).toBe('tableCell'));
    table.child(0).forEach((cell) => expect(cell.type.name).toBe('tableHeader'));
  });

  it('RunTableOp_WhenAddRowAfterOnTableOfOnlyAHeaderRow_ShouldCreateBodyCells', () => {
    const e = tableEditor(1);
    select(e, [0, 1]);

    runTableOp(e, 'addRowAfter');

    const table = tableOf(e);
    expect(table.childCount).toBe(2);
    table.child(1).forEach((cell) => expect(cell.type.name).toBe('tableCell'));
  });

  it('RunTableOp_WhenAddRowAfterWithCaretInHeaderCell_ShouldCreateBodyCells', () => {
    const e = tableEditor(2);
    e.view.dispatch(
      e.state.tr.setSelection(TextSelection.create(e.state.doc, cellPos(e, 0, 1) + 2)),
    );

    runTableOp(e, 'addRowAfter');

    const table = tableOf(e);
    expect(table.childCount).toBe(3);
    table.child(1).forEach((cell) => expect(cell.type.name).toBe('tableCell'));
  });

  it('RunTableOp_WhenDeleteColumnSelectsEveryColumn_ShouldDeleteTheTable', () => {
    const e = tableEditor();
    select(e, [1, 0], [1, 2]);

    runTableOp(e, 'deleteColumn');

    expect(e.state.doc.childCount).toBe(1);
    expect(e.state.doc.firstChild!.type.name).not.toBe('table');
  });
});
