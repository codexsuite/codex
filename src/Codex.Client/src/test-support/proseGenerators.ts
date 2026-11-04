import fc from 'fast-check';
import type { Node as PMNode } from '@tiptap/pm/model';
import { proseSchema, summarySchema } from '../prose/schema/schemas';

const schema = proseSchema;
const WORD_CHARS = [...'abcXYZ019 .,;:!?\'"()-/#>+~=&@é日本ß  ’“”—…😀*_[]\\`|<{}'];
const SAFE_CHARS = WORD_CHARS.filter((c) => c !== '`');

const text = fc
  .array(fc.constantFrom(...WORD_CHARS), { minLength: 1, maxLength: 24 })
  .map((cs) => cs.join(''));
const label = fc
  .array(fc.constantFrom(...SAFE_CHARS.filter((c) => c !== ' ')), { minLength: 1, maxLength: 12 })
  .map((cs) => cs.join('').trim() || 'x');
const id = fc.uuid();

const HREFS = ['https://example.com/a?b=1', 'http://x.org', 'mailto:a@b.c', 'https://x.io/(p)/a b'];
const latexSource = fc.stringMatching(/^[ -_a-~]{1,16}$/);

interface Segment {
  kind: 'text' | 'mention' | 'refMarker' | 'latex';
  text: string;
  id: string;
  bold: boolean;
  italic: boolean;
  code: boolean;
  strike: boolean;
  href: string | null;
  source: string;
  ref: string | null;
  chapter: string | null;
}

function segmentArbitrary(links: boolean): fc.Arbitrary<Segment> {
  return fc.record({
    kind: fc.constantFrom<Segment['kind']>(
      ...(links
        ? (['text', 'text', 'text', 'mention', 'refMarker', 'latex'] as const)
        : (['text', 'text', 'mention'] as const)),
    ),
    text,
    id,
    bold: fc.boolean(),
    italic: fc.boolean(),
    code: fc.integer({ min: 0, max: 5 }).map((n) => n === 0),
    strike: links ? fc.integer({ min: 0, max: 5 }).map((n) => n === 0) : fc.constant(false),
    href: links ? fc.option(fc.constantFrom(...HREFS), { nil: null, freq: 5 }) : fc.constant(null),
    source: latexSource,
    ref: links ? fc.option(id, { nil: null, freq: 4 }) : fc.constant(null),
    chapter: links ? fc.option(id, { nil: null, freq: 8 }) : fc.constant(null),
  });
}

function toNodes(s: Segment, sch: typeof schema): PMNode {
  const marks = [];
  if (s.bold) marks.push(sch.marks.bold.create());
  if (s.italic) marks.push(sch.marks.italic.create());
  if (s.strike && sch.marks.strike) marks.push(sch.marks.strike.create());
  if (s.ref && sch.marks.ref) marks.push(sch.marks.ref.create({ id: s.ref }));
  // Link text holds no bracket, so a link never wraps a mention, a marker or a ref mark.
  if (s.href && s.kind === 'text' && !s.ref && !s.chapter && sch.marks.link)
    marks.push(sch.marks.link.create({ href: s.href }));
  if (s.kind === 'latex') return sch.nodes.rawLatexInline.create({ source: s.source }, null, marks);
  if (s.kind === 'mention')
    return sch.nodes.mention.create({ id: s.id, label: label0(s.text) }, null, marks);
  if (s.kind === 'refMarker') return sch.nodes.refMarker.create({ id: s.id }, null, marks);
  if (s.code) return sch.text(s.text.replace(/`/g, '') || 'a', [...marks, sch.marks.code.create()]);
  if (s.chapter && sch.marks.chapterLink && marks.length === 0)
    return sch.text(s.text, [sch.marks.chapterLink.create({ id: s.chapter })]);
  return sch.text(s.text, marks);
}

function label0(t: string): string {
  return t.trim() || 'x';
}

function lineNodes(segments: Segment[], sch: typeof schema): PMNode[] {
  const nodes = segments.map((s) => toNodes(s, sch));
  const first = nodes[0];
  const last = nodes[nodes.length - 1];
  if (first?.isText) nodes[0] = sch.text(first.text!.trimStart() || 'a', first.marks);
  if (last?.isText) nodes[nodes.length - 1] = sch.text(last.text!.trimEnd() || 'a', last.marks);
  return nodes;
}

function paragraphNodes(lines: Segment[][], sch: typeof schema): PMNode[] {
  const out: PMNode[] = [];
  lines.forEach((segments, index) => {
    if (index > 0) out.push(sch.nodes.hardBreak.create());
    out.push(...lineNodes(segments, sch));
  });
  return out;
}

const lines = (links: boolean, breaks: boolean) =>
  fc.array(fc.array(segmentArbitrary(links), { minLength: 1, maxLength: 6 }), {
    minLength: 1,
    maxLength: breaks ? 3 : 1,
  });

const paragraph = lines(true, true).map((ls) =>
  schema.nodes.paragraph.create(null, paragraphNodes(ls, schema)),
);
const heading = fc
  .tuple(fc.integer({ min: 1, max: 6 }), lines(true, false))
  .map(([level, ls]) => schema.nodes.heading.create({ level }, paragraphNodes(ls, schema)));
const directive = fc.oneof(
  fc
    .constantFrom('beat', 'kb-addition', 'figure')
    .chain((name) => id.map((ref) => schema.nodes.directive.create({ name, ref }))),
  fc
    .constantFrom('spell', 'statblock')
    .chain((name) => id.map((ref) => schema.nodes.directive.create({ name, ref }))),
  fc.constant(schema.nodes.directive.create({ name: 'pagebreak', ref: null })),
);
const latexLine = fc.stringMatching(/^[ -~]{0,20}$/).filter((l) => l !== '```');
const rawLatex = fc
  .array(latexLine, { minLength: 0, maxLength: 4 })
  .map((ls) => schema.nodes.rawLatex.create({ source: ls.join('\n') }));
const rule = fc.constant(schema.nodes.horizontalRule.create());
const alignment = fc.constantFrom<string | null>(null, 'left', 'center', 'right');
const tableCell = fc
  .option(lines(true, false), { nil: null, freq: 5 })
  .map((ls) => schema.nodes.paragraph.create(null, ls === null ? [] : paragraphNodes(ls, schema)));
const table = fc
  .tuple(fc.integer({ min: 1, max: 3 }), fc.integer({ min: 1, max: 3 }))
  .chain(([columns, rows]) =>
    fc.tuple(
      fc.array(alignment, { minLength: columns, maxLength: columns }),
      fc.array(fc.array(tableCell, { minLength: columns, maxLength: columns }), {
        minLength: rows,
        maxLength: rows,
      }),
    ),
  )
  .map(([align, rows]) =>
    schema.nodes.table.create(
      { align, source: null },
      rows.map((cells, r) =>
        schema.nodes.tableRow.create(
          null,
          cells.map((p) =>
            (r === 0 ? schema.nodes.tableHeader : schema.nodes.tableCell).create(null, p),
          ),
        ),
      ),
    ),
  );
const opaque = fc
  .constantFrom(
    '<!-- a note -->',
    '<!-- first\n\nsecond | line\n-->',
    '::future{id="x" extra="y"}',
    ':::mystery\nbody\n\nmore body\n:::',
    '```python\nprint(1)\n\nprint(2)\n```',
    '::pagebreak{now="1"}',
    ':::mystery{kind="z"}',
  )
  .map((raw) => schema.nodes.opaqueBlock.create({ raw }));

// Containers do not nest, so a container's own children never include an unknown container (it would close the outer one).
const inContainer = (n: PMNode) =>
  !(n.type.name === 'opaqueBlock' && (n.attrs.raw as string).endsWith('\n:::'));
const leafBlock = fc.oneof(
  { weight: 6, arbitrary: paragraph },
  { weight: 2, arbitrary: heading },
  { weight: 2, arbitrary: directive },
  { weight: 1, arbitrary: rawLatex },
  { weight: 2, arbitrary: table },
  { weight: 1, arbitrary: rule },
  { weight: 1, arbitrary: opaque },
);

interface Blocks {
  list: fc.Arbitrary<PMNode>;
  quote: fc.Arbitrary<PMNode>;
  container: fc.Arbitrary<PMNode>;
  any: fc.Arbitrary<PMNode>;
}

const { list, quote, container, any } = fc.letrec<{
  list: PMNode;
  quote: PMNode;
  container: PMNode;
  any: PMNode;
  leaf: PMNode;
}>((tie) => ({
  leaf: leafBlock,
  list: fc
    .tuple(
      fc.boolean(),
      fc.integer({ min: 1, max: 12 }),
      fc.array(
        fc.tuple(paragraph, fc.option(fc.oneof(tie('leaf'), tie('list')), { nil: null, freq: 3 })),
        { minLength: 1, maxLength: 3 },
      ),
    )
    .map(([ordered, start, items]) => {
      const itemNodes = items.map(([p, extra]) =>
        schema.nodes.listItem.create(
          null,
          extra && extra.type.name !== 'container' ? [p, extra] : [p],
        ),
      );
      return ordered
        ? schema.nodes.orderedList.create({ start }, itemNodes)
        : schema.nodes.bulletList.create(null, itemNodes);
    }),
  quote: fc
    .array(fc.oneof(tie('leaf'), tie('list')), { minLength: 1, maxLength: 3 })
    .map((children) => schema.nodes.blockquote.create(null, children)),
  container: fc
    .tuple(
      fc.constantFrom('readaloud', 'sidebar', 'aside'),
      fc.option(
        label.filter((l) => !l.includes('"')),
        { nil: null },
      ),
      fc.array(fc.oneof(tie('leaf'), tie('list'), tie('quote')).filter(inContainer), {
        minLength: 1,
        maxLength: 3,
      }),
    )
    .map(([name, title, children]) =>
      schema.nodes.container.create({ name, title: name === 'sidebar' ? title : null }, children),
    ),
  any: fc.oneof(
    { weight: 4, arbitrary: tie('leaf') },
    { weight: 2, arbitrary: tie('list') },
    { weight: 1, arbitrary: tie('quote') },
    { weight: 1, arbitrary: tie('container') },
  ),
})) as unknown as Blocks;
void list;
void quote;
void container;

export const proseDocumentArbitrary: fc.Arbitrary<PMNode> = fc
  .array(any, { minLength: 0, maxLength: 6 })
  .map((blocks) =>
    schema.topNodeType.create(null, blocks.length > 0 ? blocks : [schema.nodes.paragraph.create()]),
  );

const summaryLine = lines(false, false).map((ls) => paragraphNodes(ls, summarySchema));
export const summaryDocumentArbitrary: fc.Arbitrary<PMNode> = summaryLine.map((nodes) =>
  summarySchema.topNodeType.create(null, summarySchema.nodes.paragraph.create(null, nodes)),
);
