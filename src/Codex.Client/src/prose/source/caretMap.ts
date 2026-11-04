import type { Node as PMNode } from '@tiptap/pm/model';

// Maps the caret across the Rich / Source toggle. The two views share no positions, only the words, so a caret is
// carried as "how many letters and digits of visible text come before it" and found again on the other side.
//
// Limits (best effort, never wrong text, only a nearby caret):
//  - Text the Markdown shows but the rich view does not draw as text is skipped on the Markdown side: directive and
//    container lines, fenced blocks, HTML comments, link destinations, inline `{=latex}` spans and ordered-list
//    numbers. A block the rich view draws as an opaque card is skipped only if it is one of those; any other
//    opaque text shifts the caret by its length.
//  - Escaped or entity-encoded characters count by what they look like in Markdown, not in the rich view.
//  - A caret inside an atom (a mention, a card) lands just after it.

const SIGNIFICANT = /[\p{L}\p{N}]/u;

// Spans of Markdown that carry no visible text.
const HIDDEN = [
  /^```[^\n]*\n[\s\S]*?^```[ \t]*$/gm,
  /^~~~[^\n]*\n[\s\S]*?^~~~[ \t]*$/gm,
  /<!--[\s\S]*?-->/g,
  /^:{2,}.*$/gm,
  /`[^`\n]+`\{=latex\}/g,
  /(?<=\])\((?:[^()\\\n]|\\.)*\)/g,
  /^[ \t]*\d+[.)](?=[ \t])/gm,
];

/** The offsets in `markdown` of each letter or digit that the rich view shows as text, in order. */
export function visibleOffsets(markdown: string): number[] {
  const hidden = new Uint8Array(markdown.length);
  for (const pattern of HIDDEN) {
    for (const m of markdown.matchAll(pattern)) hidden.fill(1, m.index, m.index + m[0].length);
  }
  const offsets: number[] = [];
  for (let i = 0; i < markdown.length;) {
    const cp = markdown.codePointAt(i) ?? 0;
    const char = String.fromCodePoint(cp);
    if (!hidden[i] && SIGNIFICANT.test(char)) offsets.push(i);
    i += char.length;
  }
  return offsets;
}

/** The text a node contributes to the count: its text, or a mention's label. */
function textOf(node: PMNode): string {
  if (node.isText) return node.text ?? '';
  return node.type.name === 'mention' ? String(node.attrs.label ?? '') : '';
}

function countSignificant(text: string): number {
  let n = 0;
  for (const char of text) if (SIGNIFICANT.test(char)) n += 1;
  return n;
}

/** How many visible letters and digits come before `pos` in the rich document. */
export function significantBefore(doc: PMNode, pos: number): number {
  let count = 0;
  doc.nodesBetween(0, Math.min(pos, doc.content.size), (node, start) => {
    const text = textOf(node);
    if (text === '') return true;
    const end = start + node.nodeSize;
    count += countSignificant(end <= pos ? text : text.slice(0, pos - start));
    return false;
  });
  return count;
}

/** The position in the rich document just after its `n`th visible letter or digit (the first text position for 0). */
export function positionAfter(doc: PMNode, n: number): number {
  let remaining = n;
  let found = -1;
  let firstText = -1;
  doc.descendants((node, start) => {
    if (found >= 0) return false;
    if (node.isTextblock && firstText < 0) firstText = start + 1;
    const text = textOf(node);
    if (text === '') return true;
    if (firstText < 0) firstText = start;
    const here = countSignificant(text);
    if (remaining > here) {
      remaining -= here;
      return false;
    }
    if (n > 0) {
      if (node.isText) {
        let seen = 0;
        let index = 0;
        for (const char of text) {
          index += char.length;
          if (SIGNIFICANT.test(char)) seen += 1;
          if (seen === remaining) break;
        }
        found = start + index;
      } else {
        found = start + node.nodeSize;
      }
    }
    return false;
  });
  if (found >= 0) return found;
  if (n > 0) return doc.content.size;
  return firstText >= 0 ? firstText : 1;
}

/** Rich → Source: the Markdown offset that matches a ProseMirror position. */
export function markdownOffsetAt(doc: PMNode, pos: number, markdown: string): number {
  const offsets = visibleOffsets(markdown);
  const n = significantBefore(doc, pos);
  if (n === 0) return offsets[0] ?? 0;
  if (n > offsets.length) return markdown.length;
  const last = offsets[n - 1];
  const after = last + String.fromCodePoint(markdown.codePointAt(last) ?? 0).length;
  // Carry the punctuation and spaces between that letter and the caret ("world.|") when the Markdown has the same.
  const anchor = positionAfter(doc, n);
  const tail = anchor < pos ? doc.textBetween(anchor, pos, '\n') : '';
  return after + commonPrefixLength(tail, markdown.slice(after));
}

function commonPrefixLength(a: string, b: string): number {
  let i = 0;
  while (i < a.length && a[i] === b[i]) i += 1;
  return i;
}

/** Source → Rich: the ProseMirror position that matches a Markdown offset. */
export function positionAtMarkdownOffset(doc: PMNode, markdown: string, offset: number): number {
  const offsets = visibleOffsets(markdown).filter((o) => o < offset);
  const anchor = positionAfter(doc, offsets.length);
  if (offsets.length === 0) return anchor;
  const last = offsets[offsets.length - 1];
  const tail = markdown.slice(
    last + String.fromCodePoint(markdown.codePointAt(last) ?? 0).length,
    offset,
  );
  const textblockEnd = doc.resolve(anchor).end();
  return anchor + commonPrefixLength(tail, doc.textBetween(anchor, textblockEnd, '\n'));
}
