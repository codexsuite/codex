import type { Editor } from '@tiptap/react';
import { TextSelection } from '@tiptap/pm/state';

/**
 * Brings the editor's selection up to date with the browser's. A caret moved by the keyboard or a tap reaches the editor
 * through `selectionchange`, which the browser delivers a moment later; an app message that reads the cursor (a command,
 * `setScene`) can arrive in that gap and would act on the old position. Reading the DOM selection first closes the gap.
 */
export function syncSelection(editor: Editor): void {
  const { view } = editor;
  if (editor.isDestroyed || !view.hasFocus()) return;
  const range = view.dom.ownerDocument.getSelection();
  if (!range) return;
  const { anchorNode, focusNode } = range;
  if (
    !anchorNode ||
    !focusNode ||
    !view.dom.contains(anchorNode) ||
    !view.dom.contains(focusNode)
  ) {
    return;
  }
  const anchor = view.posAtDOM(anchorNode, range.anchorOffset);
  const head = view.posAtDOM(focusNode, range.focusOffset);
  const { selection, doc } = view.state;
  if (selection.anchor === anchor && selection.head === head) return;
  view.dispatch(
    view.state.tr.setSelection(TextSelection.between(doc.resolve(anchor), doc.resolve(head))),
  );
}
