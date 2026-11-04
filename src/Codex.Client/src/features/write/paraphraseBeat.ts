import type { Node as PmNode } from '@tiptap/pm/model';
import { Transform } from '@tiptap/pm/transform';
import { locate, parse, serialize } from '../../prose';

/**
 * The scene body with a new `::beat` straight after the block holding the use's marker, and the marked words (the
 * beat's instruction quotes them). Null when the body has no link for the use.
 */
export function withParaphraseBeat(
  body: string,
  useId: string,
  beatId: string,
): { body: string; words: string } | null {
  const doc: PmNode = parse(body, 'prose');
  const found = locate(doc, { kind: 'ref', id: useId });
  if (found.length === 0) return null;
  const { from } = found[0];
  const to = found[found.length - 1].to;
  const after = doc.resolve(to).after(1);
  const beat = doc.type.schema.nodes.directive!.create({ name: 'beat', ref: beatId });
  return {
    body: serialize(new Transform(doc).insert(after, beat).doc),
    words: doc.textBetween(from, to, ' ', ' '),
  };
}

export function paraphraseInstruction(title: string, words: string): string {
  return `Paraphrase the passage quoted from ${title} above in your own words, keeping its meaning but none of its wording: “${words}”`;
}
