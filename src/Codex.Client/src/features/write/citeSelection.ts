import type { Node as PmNode } from '@tiptap/pm/model';
import { Plugin, PluginKey, TextSelection, type EditorState } from '@tiptap/pm/state';
import type { EditorView } from '@tiptap/pm/view';

// What `/` → Source reference cites. Typing `/` over a selection inside one paragraph keeps the selection: the plugin
// remembers it and types ` /` after it instead, so the `/` menu opens and the words are still there to cite. When the
// menu goes without a pick (Esc, blur, the caret moving off the `/query`), the ` /query` goes too and Esc or blur
// gives the selection back. With no remembered selection the item cites the sentence before the caret.

export const citeSelectionKey = new PluginKey<Kept | null>('writeCiteSelection');

/** A selection kept when `/` was typed over it; `space` is where the plugin's own space before the `/` went. */
interface Kept {
  from: number;
  to: number;
  space: number;
}

export interface CiteTarget {
  from: number;
  to: number;
  /** The plugin's own space to delete before citing, when the target is a kept selection. */
  space?: number;
}

// One textblock character per position: inline leaves (mentions, hard breaks) count as one.
const textOf = (doc: PmNode, from: number, to: number) => doc.textBetween(from, to, '\n', '\ufffc');

/** Where the kept selection's ` /query` ends, while it is still in the text; null once the `/` is gone. */
function slashEnd(doc: PmNode, kept: Kept): number | null {
  if (kept.space + 2 > doc.content.size || textOf(doc, kept.space, kept.space + 2) !== ' /')
    return null;
  const blockEnd = doc.resolve(kept.space).end();
  let end = kept.space + 2;
  while (end < blockEnd && /^\S$/u.test(textOf(doc, end, end + 1))) end += 1;
  return end;
}

/** Esc or blur while the menu is open: the ` /query` goes and the selection comes back. */
function giveBack(view: EditorView): boolean {
  const kept = citeSelectionKey.getState(view.state);
  const end = kept ? slashEnd(view.state.doc, kept) : null;
  if (!kept || end === null) return false;
  const tr = view.state.tr.delete(kept.space, end);
  tr.setSelection(TextSelection.create(tr.doc, kept.from, kept.to));
  view.dispatch(tr.setMeta(citeSelectionKey, null));
  return true;
}

export function citeSelectionPlugin(): Plugin<Kept | null> {
  return new Plugin<Kept | null>({
    key: citeSelectionKey,
    state: {
      init: () => null,
      apply(tr, kept) {
        const meta = tr.getMeta(citeSelectionKey) as Kept | null | undefined;
        if (meta !== undefined) return meta;
        if (!kept || !tr.docChanged) return kept;
        return {
          from: tr.mapping.map(kept.from, 1),
          to: tr.mapping.map(kept.to, -1),
          space: tr.mapping.map(kept.space, 1),
        };
      },
    },
    // The caret left the `/query` (a click, an arrow key, typing a space): the menu is gone, and so is its text.
    appendTransaction(_transactions, _before, state) {
      const kept = citeSelectionKey.getState(state);
      const end = kept ? slashEnd(state.doc, kept) : null;
      if (!kept || end === null) return null;
      const { selection } = state;
      if (selection.empty && selection.from >= kept.space + 2 && selection.from <= end) return null;
      return state.tr.delete(kept.space, end).setMeta(citeSelectionKey, null);
    },
    props: {
      handleKeyDown: (view, event) => event.key === 'Escape' && giveBack(view),
      handleDOMEvents: {
        blur: (view) => {
          giveBack(view);
          return false;
        },
      },
      handleTextInput(view, from, to, text) {
        if (text !== '/' || from >= to) return false;
        const $from = view.state.doc.resolve(from);
        if (!$from.parent.isTextblock || !$from.sameParent(view.state.doc.resolve(to))) {
          return false;
        }
        const tr = view.state.tr.insertText(' /', to, to);
        tr.setSelection(TextSelection.create(tr.doc, to + 2));
        tr.setMeta(citeSelectionKey, { from, to, space: to });
        view.dispatch(tr);
        return true;
      },
    },
  });
}

// The kept selection, when the caret still sits after its ` /` (and the `/query` typed there, while the menu is open).
function keptTarget(state: EditorState): CiteTarget | null {
  const kept = citeSelectionKey.getState(state);
  const { selection, doc } = state;
  if (!kept || !selection.empty || kept.from >= kept.to) return null;
  const caret = selection.from;
  if (caret <= kept.space || textOf(doc, kept.space, kept.space + 1) !== ' ') return null;
  const $space = doc.resolve(kept.space);
  if (!$space.sameParent(doc.resolve(caret)) || !$space.sameParent(doc.resolve(kept.from))) {
    return null;
  }
  if (!/^(\/\S*)?$/.test(textOf(doc, kept.space + 1, caret))) return null;
  return kept;
}

/** Where the last sentence of `text` starts and ends (trailing whitespace and a `/query` left out), or null. */
export function lastSentence(text: string): { start: number; end: number } | null {
  const body = text.replace(/\/\S*$/, '').trimEnd();
  if (!/[\p{L}\p{N}]/u.test(body)) return null;
  // A sentence ends at . ! or ? (and any closing quotes or brackets) followed by whitespace; `body` ends in neither.
  const boundary = /[.!?]['"”’)\]]*\s+/g;
  let start = 0;
  for (let match = boundary.exec(body); match; match = boundary.exec(body)) {
    start = match.index + match[0].length;
  }
  const sentence = body.slice(start);
  if (!/[\p{L}\p{N}]/u.test(sentence)) return null;
  return { start, end: body.length };
}

/** The sentence before the caret in its paragraph. */
function sentenceTarget(state: EditorState): CiteTarget | null {
  const { selection, doc } = state;
  if (!selection.empty) return null;
  const $caret = selection.$from;
  if (!$caret.parent.isTextblock) return null;
  const blockStart = $caret.start();
  const found = lastSentence(textOf(doc, blockStart, $caret.pos));
  return found ? { from: blockStart + found.start, to: blockStart + found.end } : null;
}

/** What Source reference would cite now: the kept selection, else the sentence before the caret; null for nothing. */
export function citeTarget(state: EditorState): CiteTarget | null {
  return keptTarget(state) ?? sentenceTarget(state);
}

/**
 * Another `/` item picked after `/` over a selection: the menu has taken its `/query`, so the space the plugin typed
 * goes too and the kept selection is forgotten. The caret stays where the words end.
 */
export function releaseKeptSelection(view: EditorView): void {
  if (!citeSelectionKey.getState(view.state)) return;
  const target = keptTarget(view.state);
  const tr = view.state.tr.setMeta(citeSelectionKey, null);
  if (target?.space !== undefined) tr.delete(target.space, target.space + 1);
  view.dispatch(tr);
}
