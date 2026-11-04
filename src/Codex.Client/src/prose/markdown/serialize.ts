import {
  Mark as MarkClass,
  type Fragment,
  type Mark,
  type Node as PMNode,
  type Schema,
} from '@tiptap/pm/model';
import { DIRECTIVE_ATTRIBUTE, type DirectiveName } from '../schema/nodes';
import {
  EXTERNAL_HREF,
  markAttributes,
  parseInline,
  type InlineOptions,
  type Leaf,
} from './inlineParse';
import { parse } from './parse';

type Item =
  | { kind: 'text'; text: string; marks: Mark[] }
  | { kind: 'atom'; markdown: string; marks: Mark[] }
  | { kind: 'break' };

// Outermost first when two marks cover the same run. Code is always innermost: a code span holds raw text.
const MARK_ORDER = ['ref', 'chapterLink', 'link', 'bold', 'italic', 'strike', 'code'];

/** Serializes a document of either variant back to the stored Markdown. `serialize(parse(x)) === x` for canonical `x`. */
export function serialize(doc: PMNode): string {
  if (!doc.type.schema.nodes.blockquote) {
    return inline(doc.firstChild?.content ?? doc.content, false);
  }
  const blocks = blockStrings(doc.content);
  return blocks.length === 0 ? '' : `${blocks.join('\n\n')}\n`;
}

function blockStrings(content: Fragment): string[] {
  const out: string[] = [];
  content.forEach((node) => {
    const text = block(node);
    if (text !== '') out.push(text);
  });
  return out;
}

/** `inItem`: the node is a child of a list item, where a list marker line would start a nested list. */
function block(node: PMNode, inItem = false): string {
  switch (node.type.name) {
    case 'paragraph':
      return escapeLineStarts(inline(node.content, true).replace(/ +(?=\n|$)/g, ''), inItem);
    case 'heading': {
      const text = inline(node.content, false);
      return text === '' ? '' : `${'#'.repeat(node.attrs.level as number)} ${text}`;
    }
    case 'blockquote': {
      const inner = blockStrings(node.content).join('\n\n');
      return inner === ''
        ? '>'
        : inner
            .split('\n')
            .map((l) => (l === '' ? '>' : `> ${l}`))
            .join('\n');
    }
    case 'bulletList':
      return list(node, () => '- ');
    case 'orderedList': {
      const start = (node.attrs.start as number | null) ?? 1;
      return list(node, (index) => `${start + index}. `);
    }
    case 'horizontalRule':
      return '---';
    case 'table':
      return table(node);
    case 'directive': {
      const name = node.attrs.name as DirectiveName;
      const key = DIRECTIVE_ATTRIBUTE[name];
      return key === null ? `::${name}` : `::${name}{${key}="${node.attrs.ref as string}"}`;
    }
    case 'container': {
      const title = node.attrs.title as string | null;
      const header = `:::${node.attrs.name as string}${title === null ? '' : `{title="${title}"}`}`;
      const inner = blockStrings(node.content).join('\n\n');
      return inner === '' ? `${header}\n:::` : `${header}\n${inner}\n:::`;
    }
    case 'rawLatex': {
      const source = node.attrs.source as string;
      return source === '' ? '```{=latex}\n```' : `\`\`\`{=latex}\n${source}\n\`\`\``;
    }
    case 'opaqueBlock':
      return node.attrs.raw as string;
    default:
      return '';
  }
}

function list(node: PMNode, marker: (index: number) => string): string {
  const items: string[] = [];
  node.forEach((item, _offset, index) => {
    const prefix = marker(index);
    const inner = itemContent(item);
    if (inner === '') {
      items.push(prefix.trimEnd());
      return;
    }
    const pad = ' '.repeat(prefix.length);
    items.push(
      inner
        .split('\n')
        .map((l, n) => (n === 0 ? prefix + l : l === '' ? '' : pad + l))
        .join('\n'),
    );
  });
  return items.join('\n');
}

/** An item's blocks: a list straight after a paragraph is that item's nested list, with no blank line between. */
function itemContent(item: PMNode): string {
  let out = '';
  let last: PMNode | null = null;
  item.forEach((node) => {
    const text = block(node, true);
    if (text === '') return;
    const nested =
      last?.type.name === 'paragraph' &&
      (node.type.name === 'bulletList' || node.type.name === 'orderedList');
    out = last === null ? text : `${out}${nested ? '\n' : '\n\n'}${text}`;
    last = node;
  });
  return out;
}

/** A paragraph line that would read as another block gets a backslash. */
function escapeLineStarts(text: string, inItem: boolean): string {
  return text
    .split('\n')
    .map((line, n) => {
      if (line.startsWith('::')) return `\\${line}`;
      if (n > 0) return inItem ? escapeListMarker(line) : line;
      if (
        /^(#{1,6}|>|-)(?: |$)/.test(line) ||
        line.startsWith('>') ||
        line.startsWith('|') ||
        line.startsWith('<!--') ||
        line === '---'
      )
        return `\\${line}`;
      return escapeListMarker(line);
    })
    .join('\n');
}

function escapeListMarker(line: string): string {
  if (/^-(?: |$)/.test(line)) return `\\${line}`;
  const ordered = /^(\d+)\.(?: |$)/.exec(line);
  return ordered ? `${ordered[1]}\\${line.slice(ordered[1].length)}` : line;
}

/** A table written as it was read while its content is unchanged, otherwise padded to its widest cells. */
function table(node: PMNode): string {
  const source = node.attrs.source as string | null;
  if (source !== null && source !== '' && unchanged(node, source)) return source;
  const rows: string[][] = [];
  node.forEach((row) => {
    const cells: string[] = [];
    row.forEach((cell) => {
      const text = inline(cell.firstChild?.content ?? cell.content, false);
      cells.push(text.replace(/^[ \t]+|[ \t]+$/g, '').replace(/\|/g, '\\|'));
    });
    rows.push(cells);
  });
  const align = node.attrs.align as (string | null)[];
  const columns = Math.max(...rows.map((r) => r.length));
  const widths = Array.from({ length: columns }, (_, c) =>
    Math.max(1, ...rows.map((r) => (r[c] ?? '').length)),
  );
  const line = (cells: string[]) =>
    `| ${widths.map((w, c) => (cells[c] ?? '').padEnd(w)).join(' | ')} |`;
  const delimiter = widths.map((w, c) => {
    const dashes = '-'.repeat(w + 2);
    switch (align[c]) {
      case 'left':
        return `:${dashes.slice(1)}`;
      case 'right':
        return `${dashes.slice(1)}:`;
      case 'center':
        return `:${dashes.slice(2)}:`;
      default:
        return dashes;
    }
  });
  return [line(rows[0]), `|${delimiter.join('|')}|`, ...rows.slice(1).map(line)].join('\n');
}

function unchanged(node: PMNode, source: string): boolean {
  const read = parse(source, 'prose').firstChild;
  return (
    read?.type.name === 'table' &&
    JSON.stringify(read.attrs.align) === JSON.stringify(node.attrs.align) &&
    read.content.eq(node.content)
  );
}

function inline(content: Fragment, breaks: boolean): string {
  const items: Item[] = [];
  content.forEach((child) => {
    if (child.isText) {
      items.push({ kind: 'text', text: child.text ?? '', marks: [...child.marks] });
    } else if (child.type.name === 'mention') {
      const label = escapeText(child.attrs.label as string);
      items.push({
        kind: 'atom',
        markdown: `[${label}](kb:${child.attrs.id as string})`,
        marks: [...child.marks],
      });
    } else if (child.type.name === 'refMarker') {
      items.push({
        kind: 'atom',
        markdown: `[^](ref:${child.attrs.id as string})`,
        marks: [...child.marks],
      });
    } else if (child.type.name === 'rawLatexInline') {
      const source = child.attrs.source as string;
      if (source !== '')
        items.push({ kind: 'atom', markdown: latexSpan(source), marks: [...child.marks] });
    } else if (child.type.name === 'hardBreak') {
      items.push(breaks ? { kind: 'break' } : { kind: 'text', text: ' ', marks: [] });
    }
  });
  const schema = content.firstChild?.type.schema;
  if (!schema) return '';
  const links = Boolean(schema.marks.ref);
  const options = { links, breaks };
  // Choosing delimiters can depend on marks that the Markdown cannot carry (whitespace at the edge of a run), so
  // serialize again from what the output parses to until the text stops changing.
  let out = pick(items, options);
  for (let round = 0; round < 4; round++) {
    const again = pick(itemsFromLeaves(parseInline(out, options), schema), options);
    if (again === out) break;
    out = again;
  }
  return out;
}

function pick(source: Item[], options: InlineOptions): string {
  const items = trimEmphasis(source);
  // Literal tildes are escaped only when leaving them bare would read as a strike.
  for (const tildes of [false, true]) {
    for (let strategy = 0; strategy < STRATEGIES; strategy++) {
      const out = emit(items, new Set(), { strategy, tildes, depth: 0, counter: { n: 0 } });
      if (sameContent(expand(items), expandLeaves(parseInline(out, options)))) return out;
    }
  }
  return emit(items, new Set(), { strategy: 0, tildes: false, depth: 0, counter: { n: 0 } });
}

function atomMarkdown(leaf: Exclude<Leaf, { kind: 'text' | 'break' }>): string {
  switch (leaf.kind) {
    case 'mention':
      return `[${escapeText(leaf.label)}](kb:${leaf.id})`;
    case 'refMarker':
      return `[^](ref:${leaf.id})`;
    case 'latex':
      return latexSpan(leaf.source);
  }
}

function latexSpan(source: string): string {
  return `${codeSpan(source)}{=latex}`;
}

function itemsFromLeaves(leaves: Leaf[], schema: Schema): Item[] {
  return leaves.map((leaf): Item => {
    if (leaf.kind === 'break') return { kind: 'break' };
    const marks = leaf.marks.reduce(
      (set: readonly Mark[], spec) =>
        schema.marks[spec.name].create(markAttributes(spec)).addToSet(set),
      MarkClass.none,
    );
    if (leaf.kind === 'text') return { kind: 'text', text: leaf.text, marks: [...marks] };
    return { kind: 'atom', markdown: atomMarkdown(leaf), marks: [...marks] };
  });
}

/**
 * Emphasis cannot start or end on whitespace, so a run of bold or italic loses the mark on whitespace at either end.
 * Doing it here, character by character, makes the serializer's output a fixed point of parse then serialize.
 */
function trimEmphasis(items: Item[]): Item[] {
  type Cell = { item: Item; unit: string; marks: Mark[] };
  const cells: Cell[] = items.flatMap((item): Cell[] => {
    if (item.kind === 'text')
      return [...item.text].map((unit) => ({ item, unit, marks: item.marks }));
    return [{ item, unit: '', marks: item.kind === 'atom' ? item.marks : [] }];
  });
  const isSpace = (c: Cell) => c.item.kind === 'text' && c.unit.trim() === '';
  for (const name of ['bold', 'italic', 'strike']) {
    const has = (c: Cell) => c.item.kind !== 'break' && c.marks.some((m) => m.type.name === name);
    const strip = (c: Cell) => {
      c.marks = c.marks.filter((m) => m.type.name !== name);
    };
    let start = 0;
    while (start < cells.length) {
      if (!has(cells[start])) {
        start++;
        continue;
      }
      let end = start;
      while (end + 1 < cells.length && has(cells[end + 1])) end++;
      for (let k = start; k <= end && isSpace(cells[k]); k++) strip(cells[k]);
      for (let k = end; k >= start && isSpace(cells[k]) && has(cells[k]); k--) strip(cells[k]);
      start = end + 1;
    }
  }
  const out: Item[] = [];
  for (const cell of cells) {
    const previous = out[out.length - 1];
    if (cell.item.kind !== 'text') {
      out.push(cell.item.kind === 'atom' ? { ...cell.item, marks: cell.marks } : cell.item);
    } else if (previous?.kind === 'text' && sameMarks(previous.marks, cell.marks)) {
      previous.text += cell.unit;
    } else {
      out.push({ kind: 'text', text: cell.unit, marks: cell.marks });
    }
  }
  return out;
}

function sameMarks(a: Mark[], b: Mark[]): boolean {
  return a.length === b.length && a.every((m) => b.some((n) => markKey(n) === markKey(m)));
}

// How emphasis delimiters are chosen. Strategy 0 is the plain one (`*`, `**`); the others alternate the delimiter
// character so adjacent or nested runs cannot be mistaken for one another. The serializer picks the first strategy
// whose output parses back to the same content.
const STRATEGIES = 5;

interface Context {
  strategy: number;
  /** Escape every run of two or more literal `~`. */
  tildes: boolean;
  depth: number;
  counter: { n: number };
}

interface Cell {
  unit: string;
  marks: string;
}

function expand(items: Item[]): Cell[] {
  const cells: Cell[] = [];
  for (const item of items) {
    if (item.kind === 'break') cells.push({ unit: '\n', marks: '' });
    else if (item.kind === 'text') pushText(cells, item.text, item.marks.map(markKey));
    else cells.push({ unit: item.markdown, marks: item.marks.map(markKey).sort().join(',') });
  }
  return cells;
}

function expandLeaves(leaves: Leaf[]): Cell[] {
  const cells: Cell[] = [];
  const key = (marks: { name: string; id?: string; href?: string }[]) =>
    marks.map((m) => ((m.id ?? m.href) === undefined ? m.name : `${m.name}:${m.id ?? m.href}`));
  for (const leaf of leaves) {
    if (leaf.kind === 'break') cells.push({ unit: '\n', marks: '' });
    else if (leaf.kind === 'text') pushText(cells, leaf.text, key(leaf.marks));
    else cells.push({ unit: atomMarkdown(leaf), marks: key(leaf.marks).sort().join(',') });
  }
  return cells;
}

/** Whitespace carries no emphasis (a mark cannot start or end on it), so it is compared without bold and italic. */
function pushText(cells: Cell[], text: string, marks: string[]) {
  const bare = marks
    .filter((m) => m !== 'bold' && m !== 'italic' && m !== 'strike')
    .sort()
    .join(',');
  const full = marks.slice().sort().join(',');
  for (const unit of text) cells.push({ unit, marks: /\s/.test(unit) ? bare : full });
}

function sameContent(a: Cell[], b: Cell[]): boolean {
  return (
    a.length === b.length &&
    a.every((cell, i) => cell.unit === b[i].unit && cell.marks === b[i].marks)
  );
}

/** An empty HTML comment: read as nothing, but only between two backtick fences. */
const FENCE_BREAK = '<!---->';
const FENCE_BREAK_ANYWHERE = /<!---->/g;

function escapeText(text: string): string {
  return text.replace(/[\\*_`[\]]/g, (c) => `\\${c}`);
}

function markKey(mark: Mark): string {
  const value = (mark.attrs.id ?? mark.attrs.href) as string | null | undefined;
  return value == null ? mark.type.name : `${mark.type.name}:${value}`;
}

function emit(items: Item[], consumed: Set<string>, context: Context): string {
  let out = '';
  let i = 0;
  while (i < items.length) {
    const item = items[i];
    if (item.kind === 'break') {
      out += '\n';
      i++;
      continue;
    }
    const remaining = item.marks.filter((m) => !consumed.has(markKey(m)));
    const styled = remaining.filter((m) => m.type.name !== 'code');
    const codeMark = remaining.find((m) => m.type.name === 'code');
    if (styled.length === 0 && !(codeMark && item.kind === 'text')) {
      out = append(
        out,
        item.kind === 'text' ? escapeRuns(item.text, context.tildes) : item.markdown,
      );
      i++;
      continue;
    }
    if (styled.length === 0) {
      let end = i;
      let raw = '';
      while (end < items.length && isCodeText(items[end], consumed))
        raw += (items[end++] as { text: string }).text;
      out += codeSpan(raw);
      i = end;
      continue;
    }
    let best = styled[0];
    let bestEnd = runEnd(items, i, best, consumed);
    for (const mark of styled.slice(1)) {
      const end = runEnd(items, i, mark, consumed);
      const better =
        end > bestEnd ||
        (end === bestEnd &&
          MARK_ORDER.indexOf(mark.type.name) < MARK_ORDER.indexOf(best.type.name));
      if (better) {
        best = mark;
        bestEnd = end;
      }
    }
    const own = context.counter.n++;
    const inner = emit(items.slice(i, bestEnd), new Set([...consumed, markKey(best)]), {
      ...context,
      depth: context.depth + 1,
    });
    const wrapped = wrap(best, inner, out, context, own);
    // A bare `~` before an opening `~~` would make a run of three.
    if (wrapped.startsWith('~~') && endsWithDelimiter(out, '~')) out = `${out.slice(0, -1)}\\~`;
    out = append(out, wrapped);
    i = bestEnd;
  }
  return out;
}

function isCodeText(item: Item, consumed: Set<string>): boolean {
  if (item.kind !== 'text') return false;
  const remaining = item.marks.filter((m) => !consumed.has(markKey(m)));
  return remaining.length === 1 && remaining[0].type.name === 'code';
}

function runEnd(items: Item[], from: number, mark: Mark, consumed: Set<string>): number {
  const key = markKey(mark);
  let end = from;
  while (end < items.length) {
    const item = items[end];
    if (item.kind === 'break' || !item.marks.some((m) => markKey(m) === key && !consumed.has(key)))
      break;
    end++;
  }
  return end;
}

/** Joins output so the end of one piece and the start of the next cannot read as one construct. */
function append(out: string, piece: string): string {
  const clash =
    (piece.startsWith('~') && endsWithDelimiter(out, '~')) ||
    (piece.startsWith('{=latex}') && endsWithDelimiter(out, '`'));
  // Two backtick fences side by side read as one longer fence, so a chip right after a code span is kept apart.
  if (piece.startsWith('`') && endsWithDelimiter(out, '`')) return `${out}${FENCE_BREAK}${piece}`;
  return `${out}${clash ? '\\' : ''}${piece}`;
}

function escapeRuns(text: string, tildes: boolean): string {
  const escaped = escapeText(text).replace(FENCE_BREAK_ANYWHERE, '\\$&');
  return tildes ? escaped.replace(/~{2,}/g, (run) => run.replace(/~/g, '\\~')) : escaped;
}

/** A span's text is padded with one space when it starts or ends with a backtick, or with a space on both sides. */
function codeSpan(raw: string): string {
  let longest = 0;
  for (const run of raw.match(/`+/g) ?? []) longest = Math.max(longest, run.length);
  const fence = '`'.repeat(longest + 1);
  const pad =
    raw.startsWith('`') ||
    raw.endsWith('`') ||
    (raw.length > 1 && raw.startsWith(' ') && raw.endsWith(' ') && raw.trim() !== '');
  return pad ? `${fence} ${raw} ${fence}` : `${fence}${raw}${fence}`;
}

function wrap(mark: Mark, inner: string, before: string, context: Context, own: number): string {
  const name = mark.type.name;
  if (name === 'ref') return `[${inner}](ref:${mark.attrs.id as string})`;
  if (name === 'chapterLink') return `[${inner}](chapter:${mark.attrs.id as string})`;
  if (name === 'link') return link(inner, mark.attrs.href as string | null);
  const core = inner.trim();
  if (core === '') return inner;
  const lead = inner.slice(0, inner.indexOf(core));
  const trail = inner.slice(lead.length + core.length);
  if (name === 'strike') return `${lead}~~${edgeTildes(core)}~~${trail}`;
  // A closing run followed at once by an opening run of the same character cannot be told apart from one longer
  // run, so the opener uses the other character.
  const char = delimiterChar(name, context, own, lead === '' && endsWithDelimiter(before, '*'));
  const delimiter = name === 'bold' ? char.repeat(2) : char;
  return `${lead}${delimiter}${core}${delimiter}${trail}`;
}

/** A link whose target the reader would not take as one is dropped, keeping its text. */
function link(inner: string, href: string | null): string {
  if (href === null || inner === '' || !EXTERNAL_HREF.test(href.replace(/[\s()<>]/g, '-')))
    return inner;
  const target = href.replace(/[\s()<>]/g, (c) =>
    c.charCodeAt(0) < 128
      ? `%${c.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0')}`
      : encodeURIComponent(c),
  );
  return `[${inner}](${target})`;
}

/** Tildes against the delimiters would join them into a longer run. */
function edgeTildes(core: string): string {
  const start = core.startsWith('~') ? `\\${core}` : core;
  return endsWithDelimiter(start, '~') ? `${start.slice(0, -1)}\\~` : start;
}

function delimiterChar(name: string, context: Context, own: number, followsStar: boolean): string {
  switch (context.strategy) {
    case 0:
      return followsStar ? '_' : '*';
    case 1:
      return context.depth % 2 === 0 ? '*' : '_';
    case 2:
      return own % 2 === 0 ? '*' : '_';
    case 3:
      return name === 'bold' ? '*' : '_';
    default:
      return name === 'bold' ? '_' : '*';
  }
}

function endsWithDelimiter(text: string, char: string): boolean {
  if (!text.endsWith(char)) return false;
  let slashes = 0;
  while (text[text.length - 2 - slashes] === '\\') slashes++;
  return slashes % 2 === 0;
}
