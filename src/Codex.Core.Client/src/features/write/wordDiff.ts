export interface DiffPart {
  kind: 'same' | 'added' | 'removed';
  text: string;
}

export interface WordDiff {
  parts: DiffPart[];
  added: number;
  removed: number;
}

// Beyond this many cells in the middle part the exact diff is skipped (the whole middle reads as replaced).
const MAX_CELLS = 4_000_000;

const tokens = (text: string) => text.match(/\S+|\s+/g) ?? [];
const isWord = (token: string) => /\S/.test(token);

/**
 * A word-level comparison of `before` against `after`: words only in `after` are `added`, words only in `before`
 * are `removed`. Whitespace travels with the text, so the parts joined (without `removed`) are `after`.
 */
export function wordDiff(before: string, after: string): WordDiff {
  const a = tokens(before);
  const b = tokens(after);
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) start += 1;
  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA -= 1;
    endB -= 1;
  }
  const parts: DiffPart[] = [];
  const push = (kind: DiffPart['kind'], list: string[]) => {
    if (list.length === 0) return;
    const text = list.join('');
    const last = parts[parts.length - 1];
    if (last && last.kind === kind) last.text += text;
    else parts.push({ kind, text });
  };
  const midA = a.slice(start, endA);
  const midB = b.slice(start, endB);
  push('same', a.slice(0, start));
  if (midA.length * midB.length > MAX_CELLS) {
    push('removed', midA);
    push('added', midB);
  } else {
    const width = midB.length + 1;
    const table = new Uint32Array((midA.length + 1) * width);
    for (let i = midA.length - 1; i >= 0; i -= 1) {
      for (let j = midB.length - 1; j >= 0; j -= 1) {
        table[i * width + j] =
          midA[i] === midB[j]
            ? table[(i + 1) * width + j + 1] + 1
            : Math.max(table[(i + 1) * width + j], table[i * width + j + 1]);
      }
    }
    let i = 0;
    let j = 0;
    while (i < midA.length && j < midB.length) {
      if (midA[i] === midB[j]) {
        push('same', [midA[i]]);
        i += 1;
        j += 1;
      } else if (table[(i + 1) * width + j] >= table[i * width + j + 1]) {
        push('removed', [midA[i]]);
        i += 1;
      } else {
        push('added', [midB[j]]);
        j += 1;
      }
    }
    push('removed', midA.slice(i));
    push('added', midB.slice(j));
  }
  push('same', a.slice(endA));
  const count = (kind: DiffPart['kind']) =>
    parts
      .filter((p) => p.kind === kind)
      .reduce((n, p) => n + tokens(p.text).filter(isWord).length, 0);
  return { parts, added: count('added'), removed: count('removed') };
}
