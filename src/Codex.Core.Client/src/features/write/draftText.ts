export const countWords = (text: string) => text.split(/\s+/).filter(Boolean).length;

export const paragraphs = (text: string) => text.split(/\n{2,}/).filter((p) => p.trim() !== '');

export interface DraftPart {
  text: string;
  /** The use a `[text](ref:<use>)` link names; null for plain text. A bare `[^]` marker has empty text. */
  useId: string | null;
}

const refLink = /\[([^\]]*)\]\(ref:([0-9a-f-]{36})\)/gi;

/** Splits a draft paragraph into plain text and its source-marker links (`[^]` carries no words). */
export function splitRefs(paragraph: string): DraftPart[] {
  const parts: DraftPart[] = [];
  let last = 0;
  for (const match of paragraph.matchAll(refLink)) {
    if (match.index > last) parts.push({ text: paragraph.slice(last, match.index), useId: null });
    parts.push({ text: match[1] === '^' ? '' : match[1]!, useId: match[2]! });
    last = match.index + match[0].length;
  }
  if (last < paragraph.length) parts.push({ text: paragraph.slice(last), useId: null });
  return parts;
}
