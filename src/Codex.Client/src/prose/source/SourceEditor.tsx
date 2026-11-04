import { useEffect, useRef } from 'react';
import { Annotation, Compartment, EditorState } from '@codemirror/state';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import {
  EditorView,
  drawSelection,
  keymap,
  placeholder as placeholderExtension,
} from '@codemirror/view';
import { markdownHighlighting } from './markdownLanguage';
import { proseDecorations } from './proseDecorations';
import { sourceTheme } from './sourceTheme';

export interface SourceEditorProps {
  /** The stored Markdown, shown exactly as it is. A value that differs from the text in the editor replaces it. */
  value: string;
  /** Called with the whole text on each edit; never for a `value` replaced from outside. */
  onChange?: (markdown: string) => void;
  readOnly?: boolean;
  placeholder?: string;
  testId: string;
  /** Where the caret starts, as an offset into `value`. */
  caret?: number;
  /** Called with the caret's offset whenever it moves (the Rich view reads it on the way back). */
  onCaret?: (offset: number) => void;
  /** Focus the editor when it mounts. */
  autoFocus?: boolean;
  /** The text is one line (a scene summary): an edit that would add a line break is refused. */
  singleLine?: boolean;
}

// Marks a transaction that applies a new `value` from outside, so it is not reported as an edit.
const fromOutside = Annotation.define<boolean>();

/** A CodeMirror 6 editor over the stored Markdown: highlighting for Markdown, directives and `{=latex}` fences. */
export function SourceEditor({
  value,
  onChange,
  readOnly,
  placeholder,
  testId,
  caret,
  onCaret,
  autoFocus,
  singleLine,
}: SourceEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const onChangeRef = useRef(onChange);
  const onCaretRef = useRef(onCaret);
  useEffect(() => {
    onChangeRef.current = onChange;
    onCaretRef.current = onCaret;
  });
  const editable = useRef(new Compartment());
  const first = useRef({ value, caret, autoFocus, readOnly, placeholder, testId, singleLine });

  useEffect(() => {
    const start = first.current;
    const anchor = Math.min(start.caret ?? 0, start.value.length);
    const created = new EditorView({
      parent: host.current ?? undefined,
      state: EditorState.create({
        doc: start.value,
        selection: { anchor },
        extensions: [
          history(),
          drawSelection(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          EditorView.lineWrapping,
          start.singleLine ? singleLineFilter : [],
          markdownHighlighting,
          proseDecorations,
          sourceTheme,
          EditorView.contentAttributes.of({
            'data-testid': start.testId,
            'aria-label': 'Markdown source',
          }),
          start.placeholder ? placeholderExtension(start.placeholder) : [],
          editable.current.of(editableExtensions(!start.readOnly)),
          EditorView.updateListener.of((update) => {
            if (update.docChanged && !update.transactions.some((t) => t.annotation(fromOutside))) {
              onChangeRef.current?.(update.state.doc.toString());
            }
            if (update.selectionSet || update.docChanged) {
              onCaretRef.current?.(update.state.selection.main.head);
            }
          }),
        ],
      }),
    });
    view.current = created;
    onCaretRef.current?.(anchor);
    if (start.autoFocus) created.focus();
    return () => {
      created.destroy();
      view.current = null;
    };
  }, []);

  useEffect(() => {
    const current = view.current;
    if (current && current.state.doc.toString() !== value) {
      current.dispatch({
        changes: { from: 0, to: current.state.doc.length, insert: value },
        annotations: fromOutside.of(true),
      });
    }
  }, [value]);

  useEffect(() => {
    view.current?.dispatch({
      effects: editable.current.reconfigure(editableExtensions(!readOnly)),
    });
  }, [readOnly]);

  return <div ref={host} className="prose-source-editor" />;
}

const singleLineFilter = EditorState.transactionFilter.of((tr) =>
  tr.docChanged && tr.newDoc.lines > 1 && !tr.annotation(fromOutside) ? [] : tr,
);

function editableExtensions(on: boolean) {
  return [EditorView.editable.of(on), EditorState.readOnly.of(!on)];
}
