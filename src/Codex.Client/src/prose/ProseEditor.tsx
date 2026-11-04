import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { EditorContent, type Editor } from '@tiptap/react';
import type { MentionEntry } from './entries';
import { MentionPicker } from './MentionPicker';
import { ProseRenderContext, type ProseRenderHooks } from './renderContext';
import { SlashMenu } from './SlashMenu';
import type { SlashItem } from './slashMenu';
import { proseEditorExtensions } from './editorExtensions';
import { TableToolbar } from './tables/TableToolbar';
import { LiveValue } from './suggestion/LiveValue';
import { usePopup } from './suggestion/usePopup';
import { proseTestIds } from './testIds';
import { useProseEditor } from './useProseEditor';
import './prose.css';

export interface ProseEditorProps extends ProseRenderHooks {
  /** Stored scene Markdown. A value that differs from the last `onChange` output replaces the content. */
  value: string;
  onChange?: (markdown: string) => void;
  /** Knowledge base entries the `@` picker offers. */
  entries?: readonly MentionEntry[];
  /** Commands for the `/` menu. */
  slashItems?: readonly SlashItem[];
  readOnly?: boolean;
  placeholder?: string;
  /** The editable element's `data-testid` (default `prose-editor`); a surface nested in another editor gives its own. */
  testId?: string;
  /** Called with the TipTap editor once it exists (for `locate`, scrolling and highlighting). */
  onEditor?: (editor: Editor) => void;
  children?: ReactNode;
}

const NO_ENTRIES: readonly MentionEntry[] = [];
const NO_ITEMS: readonly SlashItem[] = [];

export function ProseEditor({
  value,
  onChange,
  entries = NO_ENTRIES,
  slashItems = NO_ITEMS,
  readOnly,
  placeholder,
  testId = proseTestIds.editor,
  onEditor,
  renderDirective,
}: ProseEditorProps) {
  const mention = usePopup<MentionEntry>();
  const slash = usePopup<SlashItem>();
  const [live] = useState(() => new LiveValue({ entries, slashItems }));
  useEffect(() => {
    live.set({ entries, slashItems });
  }, [live, entries, slashItems]);
  const extensions = useMemo(
    () =>
      proseEditorExtensions(
        { entries: () => live.get().entries, slashItems: () => live.get().slashItems },
        { mention: mention.controller, slash: slash.controller },
        placeholder,
      ),
    [live, mention.controller, slash.controller, placeholder],
  );
  const editor = useProseEditor({
    variant: 'prose',
    value,
    onChange,
    extensions,
    readOnly,
    testId,
    onEditor,
  });
  const hooks = useMemo(() => ({ renderDirective }), [renderDirective]);

  return (
    <ProseRenderContext.Provider value={hooks}>
      <EditorContent editor={editor} className="prose-editor" />
      {editor && !readOnly && <TableToolbar editor={editor} />}
      {editor && mention.view && <MentionPicker view={mention.view} />}
      {editor && slash.view && <SlashMenu view={slash.view} editor={editor} />}
    </ProseRenderContext.Provider>
  );
}
