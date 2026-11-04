import { useState } from 'react';
import type { Editor } from '@tiptap/react';
import { MarkdownSurface, useEditorMode, type MentionEntry } from '../../prose';
import { AtIcon, ListIcon, QuoteIcon } from './icons';
import { iconButton, inlineField, overline } from './kbStyles';
import { kbTestIds } from './testIds';
import { wordCount } from '@codex/core/features/kb/wordCount';
import { useCanWrite } from '../../shell/bookAccess';

interface EntryTabProps {
  summary: string;
  body: string;
  /** The server's "2 hours ago" for the last edit. */
  editedLabel: string;
  entries: readonly MentionEntry[];
  onSummary: (value: string) => void;
  onBody: (value: string) => void;
}

// The formatting bar's buttons keep the editor's selection: they act on mouse down, not on focus.
const fbarButton = `${iconButton} h-[22px] w-6 text-xs font-semibold`;
const keepSelection = (event: { preventDefault: () => void }) => event.preventDefault();

// A one or two line Summary (cards, short context) and the long-form Content: open prose under one rule with a
// formatting bar (heading, quote, list, `@` mention) and a ruled footer holding the word count.
export function EntryTab({
  summary,
  body,
  editedLabel,
  entries,
  onSummary,
  onBody,
}: EntryTabProps) {
  const [editor, setEditor] = useState<Editor | null>(null);
  // Below write (#80) the entry reads: no typing and no formatting bar.
  const canWrite = useCanWrite();
  const [mode] = useEditorMode('kb-entry');
  const words = wordCount(body);
  const format = (run: (e: Editor) => void) =>
    editor && !editor.isDestroyed && mode === 'rich' && run(editor);

  return (
    <div>
      <div className="mb-1 flex min-h-6 items-center gap-1.5">
        <span className={overline}>Summary</span>
        <span className="text-xs text-muted">
          · one or two lines, shown on cards and sent as short context
        </span>
      </div>
      <textarea
        data-testid={kbTestIds.summary}
        aria-label="Summary"
        rows={2}
        placeholder={canWrite ? 'What is this, in a sentence?' : undefined}
        readOnly={!canWrite}
        value={summary}
        onChange={(event) => onSummary(event.target.value)}
        className={`-mx-2 block min-h-[2.2em] w-[calc(100%+16px)] resize-none rounded-sm px-2 py-1.5 text-base [field-sizing:content] text-text placeholder:text-muted placeholder:italic ${inlineField}`}
      />
      <div className="mt-4">
        <div className="mb-1 flex min-h-6 items-center gap-1.5">
          <span className={overline}>Content</span>
          <span
            role="toolbar"
            aria-label="Formatting"
            className={`ml-1 items-center gap-px ${canWrite && mode === 'rich' ? 'inline-flex' : 'hidden'}`}
          >
            <button
              type="button"
              data-testid={kbTestIds.format('heading')}
              title="Heading"
              aria-label="Heading"
              onMouseDown={keepSelection}
              onClick={() => format((e) => e.chain().focus().toggleHeading({ level: 3 }).run())}
              className={fbarButton}
            >
              H
            </button>
            <button
              type="button"
              data-testid={kbTestIds.format('quote')}
              title="Quote"
              aria-label="Quote"
              onMouseDown={keepSelection}
              onClick={() => format((e) => e.chain().focus().toggleBlockquote().run())}
              className={fbarButton}
            >
              <QuoteIcon />
            </button>
            <button
              type="button"
              data-testid={kbTestIds.format('list')}
              title="List"
              aria-label="List"
              onMouseDown={keepSelection}
              onClick={() => format((e) => e.chain().focus().toggleBulletList().run())}
              className={fbarButton}
            >
              <ListIcon />
            </button>
            <button
              type="button"
              data-testid={kbTestIds.format('mention')}
              title="Mention an entry (@)"
              aria-label="Mention an entry"
              onMouseDown={keepSelection}
              onClick={() => format((e) => e.chain().focus().insertContent('@').run())}
              className={fbarButton}
            >
              <AtIcon />
            </button>
          </span>
        </div>
        <div
          data-testid={kbTestIds.body}
          className="max-w-[var(--prose-width)] min-h-40 border-t border-border py-2"
        >
          <MarkdownSurface
            surface="kb-entry"
            variant="prose"
            value={body}
            onChange={onBody}
            entries={entries}
            readOnly={!canWrite}
            placeholder={
              canWrite
                ? 'The article, notes or research. Type @ to mention another entry.'
                : undefined
            }
            onEditor={setEditor}
          />
        </div>
        <div className="flex max-w-[var(--prose-width)] flex-wrap items-center gap-1.5 border-t border-border pt-2 text-xs text-muted tabular-nums">
          <span data-testid={kbTestIds.wordCount}>
            {words.toLocaleString('en-US')} {words === 1 ? 'word' : 'words'}
          </span>
          <span className="flex-1" />
          <span>Edited {editedLabel}</span>
        </div>
      </div>
    </div>
  );
}
