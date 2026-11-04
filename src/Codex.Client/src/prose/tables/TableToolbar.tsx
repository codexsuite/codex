import { useEffect, useState, type ReactNode } from 'react';
import { useEditorState, type Editor } from '@tiptap/react';
import { proseTestIds } from '../testIds';
import { alignColumn, runTableOp, tableCaret, type Align, type TableOp } from './tableCommands';

const OPS: { op: TableOp; label: string }[] = [
  { op: 'addRowBefore', label: 'Row above' },
  { op: 'addRowAfter', label: 'Row below' },
  { op: 'addColumnBefore', label: 'Column left' },
  { op: 'addColumnAfter', label: 'Column right' },
  { op: 'deleteRow', label: 'Delete row' },
  { op: 'deleteColumn', label: 'Delete column' },
  { op: 'deleteTable', label: 'Delete table' },
];

const ALIGNS: { side: Align; label: string }[] = [
  { side: null, label: 'Default' },
  { side: 'left', label: 'Left' },
  { side: 'center', label: 'Centre' },
  { side: 'right', label: 'Right' },
];

interface Where {
  row: number;
  rows: number;
  align: Align;
  top: number;
  bottom: number;
  left: number;
}

function where(editor: Editor): Where | null {
  const caret = tableCaret(editor);
  if (!caret || !editor.isEditable || !editor.isFocused) return null;
  const dom = editor.view.nodeDOM(caret.pos);
  const rect = dom instanceof HTMLElement ? dom.getBoundingClientRect() : null;
  return {
    row: caret.row,
    rows: caret.table.childCount,
    align: (caret.table.attrs.align as Align[])[caret.column] ?? null,
    top: rect?.top ?? 0,
    bottom: rect?.bottom ?? 0,
    left: rect?.left ?? 0,
  };
}

/** Floats above the table the caret is in, with the structure operations and the column's alignment. */
export function TableToolbar({ editor }: { editor: Editor }) {
  const state = useEditorState({
    editor,
    selector: ({ editor: e }) => where(e),
    equalityFn: (a, b) => JSON.stringify(a) === JSON.stringify(b),
  });
  const [, setScrolled] = useState(0);
  useEffect(() => {
    const move = () => setScrolled((n) => n + 1);
    window.addEventListener('scroll', move, true);
    window.addEventListener('resize', move);
    return () => {
      window.removeEventListener('scroll', move, true);
      window.removeEventListener('resize', move);
    };
  }, []);
  if (!state) return null;
  const current = where(editor) ?? state;
  const disabled = (op: TableOp) =>
    (op === 'addRowBefore' && current.row === 0) ||
    (op === 'deleteRow' && current.row === 0 && current.rows > 1);

  return (
    <div
      role="toolbar"
      aria-label="Table"
      className="prose-table-toolbar"
      data-testid={proseTestIds.tableToolbar}
      style={{
        position: 'fixed',
        left: current.left,
        top: current.top >= 40 ? current.top - 34 : current.bottom + 4,
      }}
      onMouseDown={(e) => e.preventDefault()}
    >
      {OPS.map(({ op, label }) => (
        <ToolbarButton
          key={op}
          testId={proseTestIds.tableOp(op)}
          disabled={disabled(op)}
          onClick={() => runTableOp(editor, op)}
        >
          {label}
        </ToolbarButton>
      ))}
      <span className="prose-table-toolbar-sep" />
      {ALIGNS.map(({ side, label }) => (
        <ToolbarButton
          key={label}
          testId={proseTestIds.tableAlign(side ?? 'default')}
          pressed={current.align === side}
          onClick={() => alignColumn(editor, side)}
        >
          {label}
        </ToolbarButton>
      ))}
    </div>
  );
}

function ToolbarButton({
  testId,
  disabled,
  pressed,
  onClick,
  children,
}: {
  testId: string;
  disabled?: boolean;
  pressed?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      className="prose-table-toolbar-button"
      data-testid={testId}
      disabled={disabled}
      aria-pressed={pressed}
      onClick={onClick}
    >
      {children}
    </button>
  );
}
