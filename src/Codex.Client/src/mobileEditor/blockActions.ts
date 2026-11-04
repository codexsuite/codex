import type { Editor } from '@tiptap/react';
import type { PageAction } from '@codex/core/editing/editorBridge';
import { splitBody } from '../features/write/blockOps';
import { serialize } from '../prose/markdown';
import { newId } from './newId';

// The two structural edits the page cannot do alone (`docs`: editorBridge `PageAction`). Only the page has the cursor, so
// it builds the finished text exactly as the desktop's `useBlockOps` does and the app sends it to the server. The editor is
// left as it is: the app answers with `setScene` (a beat) or a fresh `load` (a split).

/** The scene Markdown with a `::beat` line at the cursor. */
export function beatAction(editor: Editor): PageAction {
  const beatId = newId();
  const node = editor.schema.nodes.directive!.create({ name: 'beat', ref: beatId });
  const body = serialize(editor.state.tr.replaceSelectionWith(node, false).doc);
  return { action: 'beat', beatId, body };
}

/** The scene Markdown before and after the block the cursor is in. */
export function newSceneAction(editor: Editor): PageAction {
  const { head, tail } = splitBody(editor.state.doc, editor.state.selection.$from.index(0));
  return { action: 'newScene', headBody: head, tailBody: tail };
}
