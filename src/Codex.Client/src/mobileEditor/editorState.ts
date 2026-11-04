import type { Editor } from '@tiptap/react';
import type { PageMessage } from '@codex/core/editing/editorBridge';

export type StateMessage = Extract<PageMessage, { type: 'state' }>;

/** What the app's accessory bar needs from the editor: which marks are on at the cursor, and whether undo and redo can run. */
export function readEditorState(editor: Editor): StateMessage {
  return {
    type: 'state',
    bold: editor.isActive('bold'),
    italic: editor.isActive('italic'),
    quote: editor.isActive('blockquote'),
    canUndo: editor.can().undo(),
    canRedo: editor.can().redo(),
  };
}

/** Sends a `state` message only when it differs from the last one sent. */
export class StateReporter {
  private last = '';

  constructor(private readonly send: (message: StateMessage) => void) {}

  report(editor: Editor): void {
    if (editor.isDestroyed) return;
    const message = readEditorState(editor);
    const key = JSON.stringify(message);
    if (key === this.last) return;
    this.last = key;
    this.send(message);
  }

  /** Forget what was sent, so the next report goes out even if it is the same (the app was reloaded). */
  reset(): void {
    this.last = '';
  }
}
