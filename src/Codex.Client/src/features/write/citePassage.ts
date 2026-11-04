import type { Node as PmNode } from '@tiptap/pm/model';
import { Transform } from '@tiptap/pm/transform';
import { serialize } from '../../prose';

// A well-formed id, so the serializer writes the mark as a link (it escapes a `ref:` it would not read back).
const SENTINEL = '00000000-0000-4000-8000-0000000c17e0';

export interface Passage {
  /** The stored Markdown of the words, as `POST /api/citations` finds them in the body. */
  passage: string;
  /** Which match of `passage` in the body it is (1-based, counting overlapping matches like the server). */
  occurrence: number;
}

/** How many matches of `passage` start at or before `upTo` in `body`, overlapping ones too (as the server counts). */
export function occurrencesUpTo(body: string, passage: string, upTo = body.length): number {
  let count = 0;
  for (
    let found = body.indexOf(passage);
    found >= 0 && found <= upTo;
    found = body.indexOf(passage, found + 1)
  ) {
    count += 1;
  }
  return count;
}

const isSpace = (doc: PmNode, at: number) =>
  /^\s$/u.test(doc.textBetween(at, at + 1, '\n', '\ufffc'));

/**
 * The Markdown passage and occurrence of the words at `from`..`to`: the body is serialized with and without a
 * sentinel `ref` mark over them, and the passage is what the mark wraps. Whitespace at either end of the range is
 * left out (a double-click takes the space after a word; the server refuses a passage that starts or ends in one).
 * Null when the words cannot take a marker as they are: empty, inside code, or a mark that would change how the
 * text around them is written.
 */
export function passageAt(doc: PmNode, from: number, to: number): Passage | null {
  const { ref, code } = doc.type.schema.marks;
  if (!ref) return null;
  while (from < to && isSpace(doc, from)) from += 1;
  while (to > from && isSpace(doc, to - 1)) to -= 1;
  if (from >= to || (code && doc.rangeHasMark(from, to, code))) return null;
  const body = serialize(doc);
  const marked = serialize(new Transform(doc).addMark(from, to, ref.create({ id: SENTINEL })).doc);
  let at = 0;
  while (at < body.length && body[at] === marked[at]) at += 1;
  const close = `](ref:${SENTINEL})`;
  const end = marked.indexOf(close, at);
  if (marked[at] !== '[' || end < 0) return null;
  const passage = marked.slice(at + 1, end);
  if (passage.trim() !== passage || passage === '') return null;
  if (body.slice(at, at + passage.length) !== passage) return null;
  if (marked.slice(end + close.length) !== body.slice(at + passage.length)) return null;
  return { passage, occurrence: occurrencesUpTo(body, passage, at) };
}
