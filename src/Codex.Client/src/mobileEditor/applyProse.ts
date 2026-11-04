import type { Editor } from '@tiptap/react';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { parse, serialize } from '../prose/markdown';
import { syncSelection } from './syncSelection';

/**
 * Puts the app's text into a scene's editor (`setScene`) when it differs from what the editor holds, comparing the
 * canonical Markdown of both so a spelling difference alone is not a change. The cursor stays where it was (clamped to
 * the new document) when the editor has focus. Emits no update: the text is the app's, there is nothing to save.
 *
 * The editor gets a fresh state, so its undo history starts over: Undo must never bring back text the app has replaced
 * (it would be saved over the app's version). Returns whether it replaced the content.
 */
export function applyProse(editor: Editor, prose: string): boolean {
  const incoming = parse(prose, 'prose');
  if (serialize(editor.state.doc) === serialize(incoming)) return false;
  syncSelection(editor);
  const { from, to } = editor.state.selection;
  const keepCursor = editor.isFocused;
  const doc = editor.schema.nodeFromJSON(incoming.toJSON());
  const at = (position: number) => doc.resolve(Math.min(position, doc.content.size));
  const created = EditorState.create({
    doc,
    plugins: editor.state.plugins,
    selection: keepCursor ? TextSelection.between(at(from), at(to)) : undefined,
  });
  editor.view.updateState(created);
  return true;
}
