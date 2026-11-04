import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { Editor } from '@tiptap/react';
import type { MentionEntry } from './entries';
import { ProseEditor } from './ProseEditor';
import type { ProseRenderHooks } from './renderContext';
import type { SlashItem } from './slashMenu';
import { SummaryEditor } from './SummaryEditor';
import { markdownOffsetAt, positionAtMarkdownOffset } from './source/caretMap';
import { useEditorMode, type EditorMode, type SurfaceKey } from './source/editorModePrefs';
import { SourceEditor } from './source/SourceEditor';
import { proseTestIds } from './testIds';
import './prose.css';

interface CommonProps {
  /** Which surface this is; the Rich / Source choice is remembered per user and per surface. */
  surface: SurfaceKey;
  /** The stored Markdown. Both views edit it, and toggling never changes it. */
  value: string;
  /** `fromSource` is true when the edit was typed in the Source view. */
  onChange?: (markdown: string, fromSource: boolean) => void;
  entries?: readonly MentionEntry[];
  readOnly?: boolean;
  placeholder?: string;
  /** Called with the TipTap editor each time the Rich view creates one (it is destroyed while Source shows). */
  onEditor?: (editor: Editor) => void;
}

export type MarkdownSurfaceProps = CommonProps &
  (
    | ({
        variant: 'prose';
        slashItems?: readonly SlashItem[];
        /** The Rich view's `data-testid`, for a surface that sits inside another editor (default `prose-editor`). */
        testId?: string;
      } & ProseRenderHooks)
    | { variant: 'summary' }
  );

/**
 * A Markdown field with a Rich / Source toggle (Mod-/). Rich is `ProseEditor` (or `SummaryEditor`), Source is the
 * CodeMirror editor over the same string. Both feed the one `value` / `onChange`, so autosave, versions and conflicts
 * are the caller's, unchanged. Switching view emits nothing: the Rich view only emits once its document is edited,
 * so text that is not canonical Markdown (an import, an agent's write) is rewritten on the first rich edit, not here.
 * The caret follows across the toggle where `source/caretMap` can place it.
 */
export function MarkdownSurface(props: MarkdownSurfaceProps) {
  const { surface, value, onChange, readOnly, placeholder, variant } = props;
  const [mode, setMode] = useEditorMode(surface);
  const rich = useRef<Editor | null>(null);
  const sourceCaret = useRef(0);
  const richCaret = useRef<number | null>(null);
  const [startAt, setStartAt] = useState(0);
  const valueNow = useRef(value);
  const onEditorNow = useRef(props.onEditor);
  const { onEditor: onEditorProp } = props;
  useEffect(() => {
    valueNow.current = value;
    onEditorNow.current = onEditorProp;
  });

  const toggle = useCallback(
    (next: EditorMode) => {
      if (next === mode) return;
      if (next === 'source') {
        const editor = rich.current;
        const at =
          editor && !editor.isDestroyed
            ? markdownOffsetAt(editor.state.doc, caretPosition(editor), valueNow.current)
            : 0;
        sourceCaret.current = at;
        setStartAt(at);
        rich.current = null;
      } else {
        richCaret.current = sourceCaret.current;
      }
      setMode(next);
    },
    [mode, setMode],
  );

  const onEditor = useCallback((editor: Editor) => {
    rich.current = editor;
    const offset = richCaret.current;
    if (offset !== null) {
      richCaret.current = null;
      const pos = positionAtMarkdownOffset(editor.state.doc, valueNow.current, offset);
      editor.chain().focus().setTextSelection(pos).run();
    }
    onEditorNow.current?.(editor);
  }, []);

  const onKeyDown = (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key === '/') {
      // A surface nested in another (a beat card in a scene) takes its own shortcut; the outer one ignores it.
      const innermost =
        event.target instanceof Element ? event.target.closest('.prose-surface') : null;
      if (innermost !== event.currentTarget) return;
      event.preventDefault();
      event.stopPropagation();
      toggle(mode === 'rich' ? 'source' : 'rich');
    }
  };

  return (
    <div
      data-testid={proseTestIds.surface}
      data-mode={mode}
      className={`prose-surface prose-surface-${variant}`}
      onKeyDownCapture={onKeyDown}
    >
      <ModeToggle surface={surface} mode={mode} onChange={toggle} />
      {mode === 'source' ? (
        <SourceEditor
          value={value}
          onChange={(markdown) => onChange?.(markdown, true)}
          readOnly={readOnly}
          placeholder={placeholder}
          testId={proseTestIds.sourceEditor(surface)}
          caret={startAt}
          onCaret={(offset) => {
            sourceCaret.current = offset;
          }}
          autoFocus={startAt > 0 || undefined}
          singleLine={variant === 'summary'}
        />
      ) : props.variant === 'summary' ? (
        <SummaryEditor
          value={value}
          onChange={(markdown) => onChange?.(markdown, false)}
          entries={props.entries}
          readOnly={readOnly}
          placeholder={placeholder}
          onEditor={onEditor}
        />
      ) : (
        <ProseEditor
          value={value}
          onChange={(markdown) => onChange?.(markdown, false)}
          entries={props.entries}
          slashItems={props.slashItems}
          testId={props.testId}
          renderDirective={props.renderDirective}
          readOnly={readOnly}
          placeholder={placeholder}
          onEditor={onEditor}
        />
      )}
    </div>
  );
}

// The caret as the browser has it: ProseMirror learns of a just-moved caret (an arrow key) on a later selectionchange,
// so its own state can lag by a tick. Falls back to that state when the DOM selection is not inside the editor.
function caretPosition(editor: Editor): number {
  const { view, state } = editor;
  try {
    const selection = view.dom.ownerDocument.getSelection();
    if (selection?.focusNode && view.dom.contains(selection.focusNode)) {
      return view.posAtDOM(selection.focusNode, selection.focusOffset);
    }
  } catch {
    // the DOM position has no document position (inside a node view)
  }
  return state.selection.head;
}

function ModeToggle({
  surface,
  mode,
  onChange,
}: {
  surface: SurfaceKey;
  mode: EditorMode;
  onChange: (mode: EditorMode) => void;
}) {
  return (
    <div role="group" aria-label="Editor view" className="prose-mode-toggle">
      <button
        type="button"
        data-testid={proseTestIds.modeRich(surface)}
        aria-pressed={mode === 'rich'}
        title="Rich view (Ctrl+/)"
        onClick={() => onChange('rich')}
      >
        Rich
      </button>
      <button
        type="button"
        data-testid={proseTestIds.modeSource(surface)}
        aria-pressed={mode === 'source'}
        title="Markdown source (Ctrl+/)"
        onClick={() => onChange('source')}
      >
        Source
      </button>
    </div>
  );
}
