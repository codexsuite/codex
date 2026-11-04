import type { Editor } from '@tiptap/react';
import type { EditorCommand } from '@codex/core/editing/editorBridge';
import { insertTrigger } from './slashItems';

/** What the beat and new-scene commands do with the editor they run on (they ask the app; see `blockActions.ts`). */
export interface BlockActions {
  beat: (editor: Editor) => void;
  newScene: (editor: Editor) => void;
}

/** Runs one accessory-bar command on the scene being edited. The caller reports the new `state` afterwards. */
export function runCommand(editor: Editor, command: EditorCommand, actions: BlockActions): void {
  switch (command) {
    case 'bold':
      editor.chain().focus().toggleMark('bold').run();
      return;
    case 'italic':
      editor.chain().focus().toggleMark('italic').run();
      return;
    case 'quote':
      editor.chain().focus().toggleBlockquote().run();
      return;
    case 'slash':
      insertTrigger(editor, '/');
      return;
    case 'mention':
      insertTrigger(editor, '@');
      return;
    case 'beat':
      actions.beat(editor);
      return;
    case 'newScene':
      actions.newScene(editor);
      return;
    case 'undo':
      editor.chain().focus().undo().run();
      return;
    case 'redo':
      editor.chain().focus().redo().run();
      return;
    case 'blur':
      editor.commands.blur();
      return;
  }
}
