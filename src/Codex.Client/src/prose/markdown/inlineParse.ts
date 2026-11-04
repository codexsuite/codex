// Inline Markdown subset: backslash escapes, code spans (a span directly followed by `{=latex}` is raw LaTeX),
// `*`/`**` emphasis and `~~` strikethrough (CommonMark's delimiter algorithm with a whitespace-only flanking rule;
// `_`/`__` read the same way as `*`, and the serializer uses them only to keep two adjacent runs apart) and links:
// the schemes `kb:`, `chapter:` and `ref:`, and external `http:`, `https:` and `mailto:` targets. Anything else is text.

export interface MarkSpec {
  name: 'bold' | 'italic' | 'strike' | 'code' | 'ref' | 'chapterLink' | 'link';
  id?: string;
  href?: string;
}

export type Leaf =
  | { kind: 'text'; text: string; marks: MarkSpec[] }
  | { kind: 'mention'; id: string; label: string; marks: MarkSpec[] }
  | { kind: 'refMarker'; id: string; marks: MarkSpec[] }
  | { kind: 'latex'; source: string; marks: MarkSpec[] }
  | { kind: 'break' };

export interface InlineOptions {
  /**
   * The constructs of a scene body: `[x](ref:…)`, `[x](chapter:…)` and external links become marks, `~~` a strike
   * mark and `` `x`{=latex} `` an atom; otherwise they stay text. `kb:` mentions are always read.
   */
  links: boolean;
  /** A newline becomes a `break` leaf; otherwise it stays in the text. */
  breaks: boolean;
}

const ASCII_PUNCTUATION = '!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~';
const WHITESPACE = /\s/;
/** An embed id: a UUID in either case, as the database triggers and the C# reader require. Anything else is text. */
export const ID_PATTERN =
  '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}';
const LINK_TARGET = new RegExp(`^\\((kb|chapter|ref):(${ID_PATTERN})\\)`);
const EXTERNAL_TARGET = /\(((?:https?:\/\/|mailto:)[^\s)]+)\)/y;
/** What the serializer may write as a link target: anything else would read back as text. */
export const EXTERNAL_HREF = /^(?:https?:\/\/|mailto:)[^\s)]+$/;
const LATEX_TAG = '{=latex}';
const FENCE_BREAK = '<!---->';

/** The mark attributes a spec stands for, for `Mark.create`. */
export function markAttributes(spec: MarkSpec): { id: string } | { href: string } | null {
  if (spec.id !== undefined) return { id: spec.id };
  return spec.href === undefined ? null : { href: spec.href };
}

type Token =
  | { k: 'text'; v: string }
  | { k: 'leaves'; leaves: Leaf[] }
  | { k: 'break' }
  | { k: 'delim'; ch: string; count: number; orig: number; canOpen: boolean; canClose: boolean }
  | { k: 'style'; style: 'bold' | 'italic' | 'strike'; children: Token[] };

export function parseInline(source: string, options: InlineOptions): Leaf[] {
  return flatten(resolveEmphasis(tokenize(source, options)), [], options);
}

/** Text of a link label with only backslash escapes undone (a mention's label is plain text). */
export function unescapeText(source: string): string {
  let out = '';
  for (let i = 0; i < source.length; i++) {
    if (source[i] === '\\' && i + 1 < source.length && ASCII_PUNCTUATION.includes(source[i + 1])) {
      out += source[++i];
    } else {
      out += source[i];
    }
  }
  return out;
}

function tokenize(s: string, options: InlineOptions): Token[] {
  const tokens: Token[] = [];
  let text = '';
  const flush = () => {
    if (text) tokens.push({ k: 'text', v: text });
    text = '';
  };
  let i = 0;
  while (i < s.length) {
    const c = s[i];
    if (c === '\\' && i + 1 < s.length && ASCII_PUNCTUATION.includes(s[i + 1])) {
      text += s[i + 1];
      i += 2;
    } else if (c === '\n') {
      if (options.breaks) {
        flush();
        tokens.push({ k: 'break' });
      } else {
        text += c;
      }
      i++;
    } else if (c === '`') {
      const run = runLength(s, i, '`');
      const close = findBacktickRun(s, i + run, run);
      if (close < 0) {
        text += '`'.repeat(run);
        i += run;
      } else {
        flush();
        const source = unpad(s.slice(i + run, close));
        const latex = options.links && s.startsWith(LATEX_TAG, close + run);
        tokens.push({
          k: 'leaves',
          leaves: [
            latex
              ? { kind: 'latex', source, marks: [] }
              : { kind: 'text', text: source, marks: [{ name: 'code' }] },
          ],
        });
        i = close + run + (latex ? LATEX_TAG.length : 0);
      }
    } else if (c === '<' && text === '' && tokens.at(-1)?.k === 'leaves' && betweenFences(s, i)) {
      i += FENCE_BREAK.length;
    } else if (c === '[') {
      const link = readLink(s, i, options);
      if (link) {
        flush();
        tokens.push({ k: 'leaves', leaves: link.leaves });
        i = link.end;
      } else {
        text += c;
        i++;
      }
    } else if (c === '~' && (!options.links || runLength(s, i, '~') !== 2)) {
      // Only a run of exactly two tildes is a strike delimiter.
      const run = runLength(s, i, '~');
      text += '~'.repeat(run);
      i += run;
    } else if (c === '*' || c === '_' || c === '~') {
      const run = runLength(s, i, c);
      const prev = i > 0 ? s[i - 1] : '';
      const next = i + run < s.length ? s[i + run] : '';
      flush();
      tokens.push({
        k: 'delim',
        ch: c,
        count: run,
        orig: run,
        canOpen: next !== '' && !WHITESPACE.test(next),
        canClose: prev !== '' && !WHITESPACE.test(prev),
      });
      i += run;
    } else {
      text += c;
      i++;
    }
  }
  flush();
  return tokens;
}

/** CommonMark: one space on both sides of a span's text is padding, unless the text is only spaces. */
function unpad(source: string): string {
  return source.length > 1 && source.startsWith(' ') && source.endsWith(' ') && source.trim() !== ''
    ? source.slice(1, -1)
    : source;
}

/** `<!---->` right after a closing fence and before an opening one keeps the two apart; it is nothing else. */
function betweenFences(s: string, at: number): boolean {
  return s.startsWith(FENCE_BREAK, at) && s[at - 1] === '`' && s[at + FENCE_BREAK.length] === '`';
}

function runLength(s: string, start: number, ch: string): number {
  let n = 0;
  while (s[start + n] === ch) n++;
  return n;
}

function findBacktickRun(s: string, from: number, length: number): number {
  let i = from;
  while (i < s.length) {
    if (s[i] === '`') {
      const run = runLength(s, i, '`');
      if (run === length) return i;
      i += run;
    } else {
      i++;
    }
  }
  return -1;
}

function readLink(
  s: string,
  start: number,
  options: InlineOptions,
): { leaves: Leaf[]; end: number } | null {
  const close = findClosingBracket(s, start);
  if (close < 0) return null;
  const inner = s.slice(start + 1, close);
  const target = LINK_TARGET.exec(s.slice(close + 1, close + 1 + 300));
  if (!target) return options.links ? readExternalLink(s, inner, close, options) : null;
  const [whole, scheme, id] = target;
  const end = close + 1 + whole.length;
  if (scheme === 'kb') {
    return { leaves: [{ kind: 'mention', id, label: unescapeText(inner), marks: [] }], end };
  }
  if (!options.links) return null;
  if (scheme === 'ref' && inner === '^') {
    return { leaves: [{ kind: 'refMarker', id, marks: [] }], end };
  }
  const name = scheme === 'ref' ? 'ref' : 'chapterLink';
  const leaves = parseInline(inner, options).map((leaf) => withMark(leaf, { name, id }));
  return { leaves, end };
}

function readExternalLink(
  s: string,
  inner: string,
  close: number,
  options: InlineOptions,
): { leaves: Leaf[]; end: number } | null {
  EXTERNAL_TARGET.lastIndex = close + 1;
  const target = EXTERNAL_TARGET.exec(s);
  if (!target || inner === '') return null;
  const href = target[1];
  const leaves = parseInline(inner, options).map((leaf) => withMark(leaf, { name: 'link', href }));
  return { leaves, end: close + 1 + target[0].length };
}

function findClosingBracket(s: string, start: number): number {
  let depth = 0;
  let i = start;
  while (i < s.length) {
    const c = s[i];
    if (c === '\\') {
      i += 2;
    } else if (c === '`') {
      const run = runLength(s, i, '`');
      const close = findBacktickRun(s, i + run, run);
      i = close < 0 ? i + run : close + run;
    } else if (c === '[') {
      // Link text holds no unescaped bracket (the triggers' and the C# reader's rule), so no link starts here.
      if (depth > 0) return -1;
      depth++;
      i++;
    } else if (c === ']') {
      depth--;
      if (depth === 0) return i;
      i++;
    } else {
      i++;
    }
  }
  return -1;
}

function withMark(leaf: Leaf, mark: MarkSpec): Leaf {
  return leaf.kind === 'break' ? leaf : { ...leaf, marks: [...leaf.marks, mark] };
}

/** CommonMark's process-emphasis over the delimiter runs, with the rule of three. */
function resolveEmphasis(tokens: Token[]): Token[] {
  let closerIndex = 0;
  while (closerIndex < tokens.length) {
    const closer = tokens[closerIndex];
    if (closer.k !== 'delim' || !closer.canClose) {
      closerIndex++;
      continue;
    }
    let openerIndex = closerIndex - 1;
    while (openerIndex >= 0 && !canMatch(tokens[openerIndex], closer)) openerIndex--;
    if (openerIndex < 0) {
      closerIndex++;
      continue;
    }
    const opener = tokens[openerIndex] as Extract<Token, { k: 'delim' }>;
    const used = opener.ch === '~' || (opener.count >= 2 && closer.count >= 2) ? 2 : 1;
    const children = tokens.splice(openerIndex + 1, closerIndex - openerIndex - 1);
    tokens.splice(openerIndex + 1, 0, {
      k: 'style',
      style: opener.ch === '~' ? 'strike' : used === 2 ? 'bold' : 'italic',
      children: resolveEmphasis(children),
    });
    opener.count -= used;
    closer.count -= used;
    closerIndex = openerIndex + 2;
    if (closer.count === 0) {
      tokens.splice(closerIndex, 1);
    }
    if (opener.count === 0) {
      tokens.splice(openerIndex, 1);
      closerIndex--;
    }
  }
  return tokens;
}

function canMatch(candidate: Token, closer: Extract<Token, { k: 'delim' }>): boolean {
  if (
    candidate.k !== 'delim' ||
    candidate.ch !== closer.ch ||
    !candidate.canOpen ||
    candidate.count === 0
  )
    return false;
  // A strike needs two tildes on each side; the rule of three is for `*` and `_`.
  if (closer.ch === '~') return candidate.count >= 2 && closer.count >= 2;
  const bothFlank = candidate.canClose || closer.canOpen;
  const sum = candidate.orig + closer.orig;
  return !(bothFlank && sum % 3 === 0 && !(candidate.orig % 3 === 0 && closer.orig % 3 === 0));
}

function flatten(tokens: Token[], marks: MarkSpec[], options: InlineOptions): Leaf[] {
  const out: Leaf[] = [];
  for (const token of tokens) {
    switch (token.k) {
      case 'text':
        out.push({ kind: 'text', text: token.v, marks });
        break;
      case 'delim':
        if (token.count > 0) out.push({ kind: 'text', text: token.ch.repeat(token.count), marks });
        break;
      case 'break':
        out.push({ kind: 'break' });
        break;
      case 'leaves':
        for (const leaf of token.leaves) {
          out.push(leaf.kind === 'break' ? leaf : { ...leaf, marks: [...marks, ...leaf.marks] });
        }
        break;
      case 'style':
        out.push(...flatten(token.children, [...marks, { name: token.style }], options));
        break;
    }
  }
  return out;
}
