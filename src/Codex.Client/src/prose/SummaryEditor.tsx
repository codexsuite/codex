import { useEffect, useMemo, useState } from 'react';
import { EditorContent, type Editor } from '@tiptap/react';
import type { MentionEntry } from './entries';
import { MentionPicker } from './MentionPicker';
import { summaryEditorExtensions } from './editorExtensions';
import { LiveValue } from './suggestion/LiveValue';
import { usePopup } from './suggestion/usePopup';
import { proseTestIds } from './testIds';
import { useProseEditor } from './useProseEditor';
import './prose.css';

export interface SummaryEditorProps {
  /** An inline scene summary: text, emphasis, code and `[Name](kb:id)` mentions on one line. */
  value: string;
  onChange?: (markdown: string) => void;
  entries?: readonly MentionEntry[];
  readOnly?: boolean;
  placeholder?: string;
  /** Called with the TipTap editor once it exists. */
  onEditor?: (editor: Editor) => void;
}

const NO_ENTRIES: readonly MentionEntry[] = [];

export function SummaryEditor({
  value,
  onChange,
  entries = NO_ENTRIES,
  readOnly,
  placeholder,
  onEditor,
}: SummaryEditorProps) {
  const mention = usePopup<MentionEntry>();
  const [live] = useState(() => new LiveValue(entries));
  useEffect(() => {
    live.set(entries);
  }, [live, entries]);
  const extensions = useMemo(
    () =>
      summaryEditorExtensions(
        { entries: () => live.get(), slashItems: () => [] },
        mention.controller,
        placeholder,
      ),
    [live, mention.controller, placeholder],
  );
  const editor = useProseEditor({
    variant: 'summary',
    value,
    onChange,
    extensions,
    readOnly,
    testId: proseTestIds.summaryEditor,
    onEditor,
  });

  return (
    <>
      <EditorContent editor={editor} className="prose-editor prose-summary-editor" />
      {editor && mention.view && <MentionPicker view={mention.view} />}
    </>
  );
}
