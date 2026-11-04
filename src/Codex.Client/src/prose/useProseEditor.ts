import { useEffect, useRef, useState } from 'react';
import { useEditor, type AnyExtension, type Editor } from '@tiptap/react';
import { parse } from './markdown/parse';
import { serialize } from './markdown/serialize';
import type { ProseVariant } from './schema/schemas';

interface Options {
  variant: ProseVariant;
  value: string;
  onChange?: (markdown: string) => void;
  extensions: AnyExtension[];
  readOnly?: boolean;
  testId: string;
  onEditor?: (editor: Editor) => void;
}

/**
 * A TipTap editor over Markdown: `value` in, `onChange(markdown)` out. A `value` that differs from the last
 * Markdown the editor produced replaces its content (a new scene, or a restore from history).
 */
export function useProseEditor({
  variant,
  value,
  onChange,
  extensions,
  readOnly,
  testId,
  onEditor,
}: Options): Editor | null {
  const last = useRef(value);
  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });
  const [initial] = useState(() => parse(value, variant).toJSON());

  const editor = useEditor({
    extensions,
    content: initial,
    editable: !readOnly,
    editorProps: { attributes: { 'data-testid': testId } },
    onUpdate: ({ editor: e }) => {
      const markdown = serialize(e.state.doc);
      last.current = markdown;
      onChangeRef.current?.(markdown);
    },
  });

  useEffect(() => {
    if (editor && value !== last.current) {
      last.current = value;
      editor.commands.setContent(parse(value, variant).toJSON(), { emitUpdate: false });
    }
  }, [editor, value, variant]);

  // No update event: by default setEditable emits one, which would re-serialize unchanged content as an edit. An empty
  // transaction tells the node views (a raw LaTeX block's own editor) that the editable state changed.
  useEffect(() => {
    if (!editor) return;
    editor.setEditable(!readOnly, false);
    editor.view.dispatch(editor.state.tr);
  }, [editor, readOnly]);

  useEffect(() => {
    if (editor) onEditor?.(editor);
  }, [editor, onEditor]);

  return editor;
}
