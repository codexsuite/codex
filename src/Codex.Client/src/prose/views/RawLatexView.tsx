import { useEffect, useRef, useSyncExternalStore } from 'react';
import { NodeViewWrapper, type Editor, type NodeViewProps } from '@tiptap/react';
import { Annotation, Compartment, EditorSelection, EditorState } from '@codemirror/state';
import { EditorView, drawSelection, keymap } from '@codemirror/view';
import { defaultKeymap } from '@codemirror/commands';
import { TextSelection } from '@tiptap/pm/state';
import { latexHighlighting } from '../source/latex';
import { sourceTheme } from '../source/sourceTheme';
import { proseTestIds } from '../testIds';

// Marks a change that copies the node's `source` into the editor, so it is not sent back to the document.
const fromDocument = Annotation.define<boolean>();

/** Whether the editor is editable now; re-read after every transaction (see `useProseEditor`). */
function useIsEditable(editor: Editor): boolean {
  return useSyncExternalStore(
    (notify) => {
      editor.on('transaction', notify);
      return () => {
        editor.off('transaction', notify);
      };
    },
    () => editor.isEditable,
  );
}

/**
 * A raw LaTeX block: a small CodeMirror editor inside the document. Typing writes `source` with `setNodeMarkup`, so
 * the document's own undo covers it; arrow keys leave the block at either end.
 */
export function RawLatexView({ node, editor, getPos }: NodeViewProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const live = useRef({ editor, getPos });
  // The block is editable exactly while the document is (it is not while a citation is being chosen, for one).
  const editable = useIsEditable(editor);
  const editableSetting = useRef(new Compartment());
  useEffect(() => {
    live.current = { editor, getPos };
  });

  useEffect(() => {
    // Moves the document's caret next to the block, then hands the keyboard back to the document.
    const leave = (forward: boolean) => () => {
      const { editor: e, getPos: where } = live.current;
      const pos = where();
      if (pos === undefined) return false;
      const target = forward ? pos + e.state.doc.nodeAt(pos)!.nodeSize : pos;
      const near = TextSelection.near(e.state.doc.resolve(target), forward ? 1 : -1);
      e.view.dispatch(e.state.tr.setSelection(near).scrollIntoView());
      e.view.focus();
      return true;
    };
    // Arrow keys leave the block when the caret is at its start (left, up on the first line) or end (right, down on the last).
    const exit = (key: string, forward: boolean) => ({
      key,
      run: (cm: EditorView) => {
        const { main } = cm.state.selection;
        if (!main.empty) return false;
        const horizontal = key === 'ArrowLeft' || key === 'ArrowRight';
        const line = cm.state.doc.lineAt(main.head);
        const onEdge = horizontal
          ? main.head === (forward ? cm.state.doc.length : 0)
          : line.number === (forward ? cm.state.doc.lines : 1);
        return onEdge && leave(forward)();
      },
    });
    const created = new EditorView({
      parent: host.current ?? undefined,
      state: EditorState.create({
        doc: live.current.editor.state.doc.nodeAt(live.current.getPos() ?? 0)?.attrs.source ?? '',
        extensions: [
          drawSelection(),
          keymap.of([
            exit('ArrowLeft', false),
            exit('ArrowUp', false),
            exit('ArrowRight', true),
            exit('ArrowDown', true),
            { key: 'Mod-z', run: () => live.current.editor.commands.undo() },
            { key: 'Mod-y', mac: 'Mod-Shift-z', run: () => live.current.editor.commands.redo() },
            { key: 'Mod-Shift-z', run: () => live.current.editor.commands.redo() },
            ...defaultKeymap,
          ]),
          EditorView.lineWrapping,
          latexHighlighting,
          sourceTheme,
          EditorView.contentAttributes.of({
            'data-testid': proseTestIds.rawLatexEditor,
            'aria-label': 'Raw LaTeX',
          }),
          editableSetting.current.of(EditorView.editable.of(live.current.editor.isEditable)),
          EditorView.updateListener.of((update) => {
            if (!update.docChanged || update.transactions.some((t) => t.annotation(fromDocument)))
              return;
            const { editor: e, getPos: where } = live.current;
            const pos = where();
            const current = pos === undefined ? null : e.state.doc.nodeAt(pos);
            if (pos === undefined || !current) return;
            e.view.dispatch(
              e.state.tr.setNodeMarkup(pos, undefined, {
                ...current.attrs,
                source: update.state.doc.toString(),
              }),
            );
          }),
        ],
      }),
    });
    view.current = created;
    return () => {
      created.destroy();
      view.current = null;
    };
  }, []);

  useEffect(() => {
    view.current?.dispatch({
      effects: editableSetting.current.reconfigure(EditorView.editable.of(editable)),
    });
  }, [editable]);

  // The document changed the source (undo, redo, a restore): show it, keeping the caret where it was.
  const source = node.attrs.source as string;
  useEffect(() => {
    const cm = view.current;
    if (!cm || cm.state.doc.toString() === source) return;
    const head = Math.min(cm.state.selection.main.head, source.length);
    cm.dispatch({
      changes: { from: 0, to: cm.state.doc.length, insert: source },
      selection: EditorSelection.cursor(head),
      annotations: fromDocument.of(true),
    });
  }, [source]);

  return (
    <NodeViewWrapper className="prose-card" data-testid={proseTestIds.rawLatexCard}>
      <span className="prose-card-label" contentEditable={false}>
        Raw LaTeX, template-specific
      </span>
      <div ref={host} className="prose-card-editor" />
    </NodeViewWrapper>
  );
}
