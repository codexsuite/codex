import { useEffect, useRef, useState } from 'react';
import { NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { proseTestIds } from '../testIds';

/**
 * An inline raw LaTeX chip. Click it, or select it and press Enter, to edit its source; emptying the source removes
 * it, and leaving it unchanged writes nothing. A source holding a newline cannot be typed into a one-line input, so
 * it is edited in the Source view.
 */
export function RawLatexInlineView({ node, editor, getPos, selected }: NodeViewProps) {
  const source = node.attrs.source as string;
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(source);
  const input = useRef<HTMLInputElement>(null);
  const oneLine = !source.includes('\n');

  useEffect(() => {
    if (!selected || editing || !editor.isEditable || !oneLine) return;
    const dom = editor.view.dom;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Enter') return;
      event.preventDefault();
      setDraft(source);
      setEditing(true);
    };
    dom.addEventListener('keydown', onKey, true);
    return () => dom.removeEventListener('keydown', onKey, true);
  }, [selected, editing, editor, source, oneLine]);

  useEffect(() => {
    if (editing) input.current?.select();
  }, [editing]);

  const finish = (commit: boolean) => {
    setEditing(false);
    const pos = getPos();
    if (commit && pos !== undefined && draft !== source) {
      const { tr } = editor.state;
      editor.view.dispatch(
        draft.trim() === ''
          ? tr.delete(pos, pos + node.nodeSize)
          : tr.setNodeMarkup(pos, undefined, { ...node.attrs, source: draft }),
      );
    }
    editor.commands.focus();
  };

  return (
    <NodeViewWrapper
      as="span"
      className="prose-raw-latex-inline"
      data-testid={proseTestIds.rawLatexInline}
      onClick={() => {
        if (!editing && editor.isEditable && oneLine) {
          setDraft(source);
          setEditing(true);
        }
      }}
    >
      {editing ? (
        <input
          ref={input}
          className="prose-raw-latex-inline-input"
          data-testid={proseTestIds.rawLatexInlineInput}
          aria-label="Raw LaTeX"
          size={Math.max(4, draft.length + 1)}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={() => finish(true)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') finish(true);
            else if (e.key === 'Escape') finish(false);
            else return;
            e.preventDefault();
            e.stopPropagation();
          }}
        />
      ) : (
        source
      )}
    </NodeViewWrapper>
  );
}
