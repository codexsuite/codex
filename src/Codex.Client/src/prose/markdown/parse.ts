import { Mark, type Node as PMNode, type Schema } from '@tiptap/pm/model';
import {
  CONTAINER_NAMES,
  DIRECTIVE_ATTRIBUTE,
  DIRECTIVE_NAMES,
  type DirectiveName,
} from '../schema/nodes';
import { schemaFor, type ProseVariant } from '../schema/schemas';
import {
  ID_PATTERN,
  markAttributes,
  parseInline,
  type InlineOptions,
  type Leaf,
} from './inlineParse';

const DIRECTIVE_LINE = /^::([a-z][a-z0-9-]*)(?:\{([a-z]+)="([^"]*)"\})?$/;
const CONTAINER_LINE = /^:::([a-z][a-z0-9-]*)(?:\{title="([^"]*)"\})?$/;
const HEADING_LINE = /^(#{1,6}) (.+)$/;
const ID = new RegExp(`^${ID_PATTERN}$`);
const BULLET_LINE = /^-(?: (.*))?$/;
const ORDERED_LINE = /^(\d+)\.(?: (.*))?$/;
const RULE_LINE = /^(?:---|\*\*\*|___)$/;
const DELIMITER_CELL = /^:?-+:?$/;

/** Parses stored Markdown into a ProseMirror document of the given variant. Never throws: what it cannot read is kept as an opaque block. */
export function parse(markdown: string, variant: ProseVariant): PMNode {
  const schema = schemaFor(variant);
  if (variant === 'summary') {
    const options: InlineOptions = { links: false, breaks: false };
    return schema.topNodeType.create(
      null,
      schema.nodes.paragraph.create(null, inlineNodes(schema, parseInline(markdown, options))),
    );
  }
  const options: InlineOptions = { links: true, breaks: true };
  const lines = markdown === '' ? [] : markdown.replace(/\n$/, '').split('\n');
  const blocks = parseBlocks(schema, lines, options);
  return schema.topNodeType.create(
    null,
    blocks.length > 0 ? blocks : [schema.nodes.paragraph.create()],
  );
}

function inlineNodes(schema: Schema, leaves: Leaf[]): PMNode[] {
  return leaves.map((leaf) => {
    if (leaf.kind === 'break') return schema.nodes.hardBreak.create();
    const marks = leaf.marks.reduce(
      (set: readonly Mark[], spec) =>
        schema.marks[spec.name].create(markAttributes(spec)).addToSet(set),
      Mark.none,
    );
    if (leaf.kind === 'text') return schema.text(leaf.text, marks);
    if (leaf.kind === 'latex')
      return schema.nodes.rawLatexInline.create({ source: leaf.source }, null, marks);
    if (leaf.kind === 'mention')
      return schema.nodes.mention.create({ id: leaf.id, label: leaf.label }, null, marks);
    return schema.nodes.refMarker.create({ id: leaf.id }, null, marks);
  });
}

/**
 * `inItem` is true for the body of a list item, where a paragraph also ends at a list marker line (the nested
 * list starts there); elsewhere a marker line is paragraph text.
 */
function parseBlocks(
  schema: Schema,
  lines: string[],
  options: InlineOptions,
  inItem = false,
): PMNode[] {
  const blocks: PMNode[] = [];
  const opaque = (raw: string) => schema.nodes.opaqueBlock.create({ raw });
  const orEmptyParagraph = (nodes: PMNode[]) =>
    nodes.length > 0 ? nodes : [schema.nodes.paragraph.create()];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (line === '') {
      i++;
    } else if (line.startsWith('```')) {
      const close = lines.indexOf('```', i + 1);
      const end = close < 0 ? trimmedEnd(lines, i) : close + 1;
      const body = lines.slice(i + 1, close < 0 ? end : close);
      if (close >= 0 && line === '```{=latex}') {
        blocks.push(schema.nodes.rawLatex.create({ source: body.join('\n') }));
      } else {
        blocks.push(opaque(lines.slice(i, end).join('\n')));
      }
      i = end;
    } else if (line.startsWith('<!--')) {
      const end = commentEnd(lines, i);
      blocks.push(opaque(lines.slice(i, end).join('\n')));
      i = end;
    } else if (RULE_LINE.test(line)) {
      blocks.push(schema.nodes.horizontalRule.create());
      i++;
    } else if (line.startsWith('|') && tableAt(lines, i)) {
      const table = parseTable(schema, lines, i, options);
      blocks.push(table.node);
      i = table.next;
    } else if (HEADING_LINE.test(line)) {
      const [, hashes, text] = HEADING_LINE.exec(line)!;
      blocks.push(
        schema.nodes.heading.create(
          { level: hashes.length },
          inlineNodes(schema, parseInline(text, options)),
        ),
      );
      i++;
    } else if (line.startsWith('>')) {
      const inner: string[] = [];
      while (i < lines.length && lines[i].startsWith('>')) {
        inner.push(lines[i].slice(lines[i][1] === ' ' ? 2 : 1));
        i++;
      }
      blocks.push(
        schema.nodes.blockquote.create(null, orEmptyParagraph(parseBlocks(schema, inner, options))),
      );
    } else if (BULLET_LINE.test(line) || ORDERED_LINE.test(line)) {
      const list = parseList(schema, lines, i, options);
      blocks.push(list.node);
      i = list.next;
    } else if (line.startsWith(':::')) {
      const header = CONTAINER_LINE.exec(line);
      const close = lines.indexOf(':::', i + 1);
      const known =
        header &&
        (CONTAINER_NAMES as readonly string[]).includes(header[1]) &&
        containerAttributesValid(header[1], header[2]);
      if (header && known && close > 0) {
        const body = parseBlocks(schema, lines.slice(i + 1, close), options);
        blocks.push(
          schema.nodes.container.create(
            { name: header[1], title: header[2] ?? null },
            orEmptyParagraph(body),
          ),
        );
        i = close + 1;
      } else if (header && close > 0) {
        blocks.push(opaque(lines.slice(i, close + 1).join('\n')));
        i = close + 1;
      } else if (header) {
        const end = trimmedEnd(lines, i);
        blocks.push(opaque(lines.slice(i, end).join('\n')));
        i = end;
      } else {
        blocks.push(opaque(line));
        i++;
      }
    } else if (line.startsWith('::')) {
      blocks.push(directiveBlock(schema, line));
      i++;
    } else {
      const start = i;
      i++;
      while (
        i < lines.length &&
        lines[i] !== '' &&
        !lines[i].startsWith('::') &&
        !lines[i].startsWith('```') &&
        !(inItem && (BULLET_LINE.test(lines[i]) || ORDERED_LINE.test(lines[i])))
      )
        i++;
      const text = lines.slice(start, i).join('\n');
      blocks.push(
        schema.nodes.paragraph.create(null, inlineNodes(schema, parseInline(text, options))),
      );
    }
  }
  return blocks;
}

/** The line after the one holding `-->`; an unclosed comment runs to the last non-blank line. */
function commentEnd(lines: string[], start: number): number {
  for (let i = start; i < lines.length; i++) {
    if (lines[i].indexOf('-->', i === start ? 4 : 0) >= 0) return i + 1;
  }
  return trimmedEnd(lines, start);
}

/** The cells of a table row line, as raw inline text: `|` ends a cell unless a backslash precedes it. */
function splitRow(line: string): string[] {
  const cells: string[] = [];
  let cell = '';
  for (let i = 1; i < line.length; i++) {
    if (line[i] === '|' && line[i - 1] !== '\\') {
      cells.push(cell);
      cell = '';
    } else {
      cell += line[i];
    }
  }
  if (cell.trim() !== '') cells.push(cell);
  return cells.map((c) => c.replace(/\\\|/g, '|').replace(/^[ \t]+|[ \t]+$/g, ''));
}

/** A table starts here when the next line is a delimiter row with as many cells as this header row. */
function tableAt(lines: string[], i: number): boolean {
  const next = lines[i + 1];
  if (next === undefined || !next.startsWith('|')) return false;
  const header = splitRow(lines[i]);
  const delimiter = splitRow(next);
  return (
    header.length > 0 &&
    delimiter.length === header.length &&
    delimiter.every((c) => DELIMITER_CELL.test(c))
  );
}

function parseTable(
  schema: Schema,
  lines: string[],
  start: number,
  options: InlineOptions,
): { node: PMNode; next: number } {
  let next = start + 2;
  while (next < lines.length && lines[next].startsWith('|')) next++;
  const rows = [splitRow(lines[start]), ...lines.slice(start + 2, next).map(splitRow)];
  const columns = Math.max(...rows.map((r) => r.length));
  const align = splitRow(lines[start + 1]).map((c) => {
    if (c.startsWith(':')) return c.endsWith(':') ? 'center' : 'left';
    return c.endsWith(':') ? 'right' : null;
  });
  while (align.length < columns) align.push(null);
  const cellText = (text: string | undefined) => {
    const content = inlineNodes(schema, parseInline(text ?? '', { ...options, breaks: false }));
    return schema.nodes.paragraph.create(null, content);
  };
  const tableRows = rows.map((row, r) =>
    schema.nodes.tableRow.create(
      null,
      Array.from({ length: columns }, (_, c) =>
        (r === 0 ? schema.nodes.tableHeader : schema.nodes.tableCell).create(
          null,
          cellText(row[c]),
        ),
      ),
    ),
  );
  const source = lines.slice(start, next).join('\n');
  return { node: schema.nodes.table.create({ align, source }, tableRows), next };
}

function trimmedEnd(lines: string[], start: number): number {
  let end = lines.length;
  while (end > start + 1 && lines[end - 1] === '') end--;
  return end;
}

function containerAttributesValid(name: string, title: string | undefined): boolean {
  return title === undefined || name === 'sidebar';
}

function directiveBlock(schema: Schema, line: string): PMNode {
  const match = DIRECTIVE_LINE.exec(line);
  if (match) {
    const [, name, key, value] = match;
    if ((DIRECTIVE_NAMES as readonly string[]).includes(name)) {
      const expected = DIRECTIVE_ATTRIBUTE[name as DirectiveName];
      if (expected === null && key === undefined)
        return schema.nodes.directive.create({ name, ref: null });
      if (expected !== null && key === expected && ID.test(value))
        return schema.nodes.directive.create({ name, ref: value });
    }
  }
  return schema.nodes.opaqueBlock.create({ raw: line });
}

function parseList(
  schema: Schema,
  lines: string[],
  start: number,
  options: InlineOptions,
): { node: PMNode; next: number } {
  const ordered = ORDERED_LINE.test(lines[start]);
  const marker = ordered ? ORDERED_LINE : BULLET_LINE;
  const items: PMNode[] = [];
  let firstNumber = 1;
  let i = start;
  while (i < lines.length) {
    const match = marker.exec(lines[i]);
    if (!match) break;
    const text = (ordered ? match[2] : match[1]) ?? '';
    const width = lines[i].length - text.length + (text === '' && !lines[i].endsWith(' ') ? 1 : 0);
    if (ordered && items.length === 0) firstNumber = Number(match[1]);
    const body = [text];
    i++;
    while (i < lines.length) {
      if (lines[i] === '') {
        let ahead = i;
        while (ahead < lines.length && lines[ahead] === '') ahead++;
        if (ahead < lines.length && lines[ahead].startsWith(' '.repeat(width))) {
          for (; i < ahead; i++) body.push('');
        } else {
          break;
        }
      } else if (lines[i].startsWith(' '.repeat(width))) {
        body.push(lines[i].slice(width));
        i++;
      } else {
        break;
      }
    }
    const blocks = parseBlocks(schema, body, options, true);
    // An item opens with a paragraph, which may be empty (`- | a |` is an empty one and then a table).
    const opening = blocks[0]?.type.name === 'paragraph' ? [] : [schema.nodes.paragraph.create()];
    items.push(schema.nodes.listItem.create(null, [...opening, ...blocks]));
  }
  const node = ordered
    ? schema.nodes.orderedList.create({ start: firstNumber }, items)
    : schema.nodes.bulletList.create(null, items);
  return { node, next: i };
}
