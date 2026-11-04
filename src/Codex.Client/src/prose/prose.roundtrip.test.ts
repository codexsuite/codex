import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { parse, serialize } from './';
import type { Mark, Node as PMNode } from '@tiptap/pm/model';
import { proseSchema } from './schema/schemas';
import { proseDocumentArbitrary, summaryDocumentArbitrary } from '../test-support/proseGenerators';

const corpusDirectory = join(process.cwd(), '../../db/prose-corpus');
const corpusFiles = readdirSync(corpusDirectory).filter(
  (f) => f.endsWith('.md') && f !== 'README.md',
);

describe('prose round trip', () => {
  it('Corpus_HasAllFiles', () => {
    expect(corpusFiles).toHaveLength(15);
  });

  it.each(corpusFiles)('Corpus_EachFile_ShouldRoundTripExactly %s', (file) => {
    const markdown = readFileSync(join(corpusDirectory, file), 'utf8');
    const doc = parse(markdown, 'prose');
    expect(() => doc.check()).not.toThrow();
    expect(serialize(doc)).toBe(markdown);
  });

  it('Generated_Documents_ShouldRoundTrip', () => {
    fc.assert(
      fc.property(proseDocumentArbitrary, (generated) => {
        const markdown = serialize(generated);
        const reparsed = parse(markdown, 'prose');
        reparsed.check();
        expect(serialize(reparsed)).toBe(markdown);
      }),
      { numRuns: 1000 },
    );
  }, 60_000);

  it('Generated_Summaries_ShouldRoundTrip', () => {
    fc.assert(
      fc.property(summaryDocumentArbitrary, (generated) => {
        const markdown = serialize(generated);
        const reparsed = parse(markdown, 'summary');
        reparsed.check();
        expect(serialize(reparsed)).toBe(markdown);
      }),
      { numRuns: 1000 },
    );
  }, 60_000);
});

describe('prose round trip, review fixes (#64)', () => {
  const { nodes, marks } = proseSchema;
  const text = (value: string, applied?: Mark[]) => proseSchema.text(value, applied);
  const paragraph = (...content: PMNode[]) =>
    proseSchema.topNodeType.create(null, nodes.paragraph.create(null, content));
  const code = (value: string) => text(value, [marks.code.create()]);
  const chip = (source: string) => nodes.rawLatexInline.create({ source });
  const chipsOf = (doc: PMNode) =>
    doc
      .firstChild!.children.filter((n) => n.type.name === 'rawLatexInline')
      .map((n) => n.attrs.source);

  it('RawLatexInline_WhenDirectlyAfterCodeSpan_ShouldKeepBothApart', () => {
    const doc = paragraph(text('a '), code('x'), chip('\\y'), text(' b'));
    const markdown = serialize(doc);
    const read = parse(markdown, 'prose');

    expect(chipsOf(read)).toEqual(['\\y']);
    expect(read.firstChild!.children.filter((n) => n.marks.length > 0).map((n) => n.text)).toEqual([
      'x',
    ]);
    expect(serialize(read)).toBe(markdown);
  });

  it('RawLatexInline_WhenCodeSpanThenChipAtParagraphStart_ShouldNotOpenAFence', () => {
    const markdown = serialize(paragraph(code('x'), chip('\\y')));
    const read = parse(markdown, 'prose');

    expect(read.firstChild!.type.name).toBe('paragraph');
    expect(chipsOf(read)).toEqual(['\\y']);
  });

  it.each(["``Hello''", '\\verb`x`', '`', 'a`', '`a'])(
    'RawLatexInline_WhenSourceHasBackticksAtAnEdge_ShouldRoundTrip %s',
    (source) => {
      const markdown = serialize(paragraph(text('a '), chip(source), text(' b')));
      const read = parse(markdown, 'prose');

      expect(chipsOf(read)).toEqual([source]);
      expect(serialize(read)).toBe(markdown);
    },
  );

  it.each(['`x', 'x`', '`x`', ' x ', ' x', 'x ', '  ', ' ` '])(
    'Code_WhenTextHasBackticksOrSpacesAtAnEdge_ShouldRoundTrip %j',
    (value) => {
      const markdown = serialize(paragraph(text('a '), code(value), text(' b')));
      const read = parse(markdown, 'prose');

      expect(read.firstChild!.children[1].text).toBe(value);
      expect(serialize(read)).toBe(markdown);
    },
  );

  it('Code_WhenWrittenWithOnePaddingSpace_ShouldReadWithoutIt', () => {
    expect(parse('`` `x` ``\n', 'prose').firstChild!.firstChild!.text).toBe('`x`');
    expect(parse('`  `\n', 'prose').firstChild!.firstChild!.text).toBe('  ');
  });

  it.each(['--- ', '| a ', '<!-- a ', '# ', '> ', '- ', '1. ', '***  '])(
    'Paragraph_WhenTrailingSpacesFollowBlockSyntax_ShouldBeAFixedPoint %j',
    (value) => {
      const markdown = serialize(paragraph(text(value)));
      const read = parse(markdown, 'prose');

      expect(read.firstChild!.type.name).toBe('paragraph');
      expect(serialize(read)).toBe(markdown);
    },
  );
});
