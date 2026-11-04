import { describe, expect, it } from 'vitest';
import type { Node as PMNode } from '@tiptap/pm/model';
import { locate, parse, serialize } from './';

const BEAT = '0199c0de-0000-7000-8000-000700000003';
const USE = '0199c0de-0000-7000-8000-000e00000001';
const KB = '0199c0de-0000-7000-8000-000b00000005';

const cellTexts = (table: PMNode) =>
  table.children.map((row) => row.children.map((cell) => cell.textContent));

function firstTable(markdown: string): PMNode {
  const table = parse(markdown, 'prose').firstChild!;
  expect(table.type.name).toBe('table');
  return table;
}

describe('prose parse', () => {
  it('Locate_WhenBeatId_ShouldReturnPosition', () => {
    const doc = parse(
      `First paragraph.\n\n::beat{id="${BEAT}"}\n\nSecond [quoted words](ref:${USE}) here.\n`,
      'prose',
    );
    const [beat] = locate(doc, { kind: 'beat', id: BEAT });
    expect(beat).toBeDefined();
    expect(doc.nodeAt(beat.from)?.attrs).toMatchObject({ name: 'beat', ref: BEAT });
    expect(beat.to - beat.from).toBe(1);

    const [ref] = locate(doc, { kind: 'ref', id: USE });
    expect(doc.textBetween(ref.from, ref.to)).toBe('quoted words');
    expect(locate(doc, { kind: 'beat', id: 'missing' })).toEqual([]);
  });

  it('Parse_WhenUnknownDirective_ShouldPreserve', () => {
    const markdown = [
      'Before.',
      '::future{id="x" extra="y"}',
      ':::mystery\nbody line\n\nsecond body\n:::',
      '```python\nprint(1)\n```',
      ':::aside\nnever closed\n',
    ].join('\n\n');
    const doc = parse(markdown, 'prose');
    const names = doc.content.content.map((n) => n.type.name);
    expect(names).toEqual([
      'paragraph',
      'opaqueBlock',
      'opaqueBlock',
      'opaqueBlock',
      'opaqueBlock',
    ]);
    expect(serialize(doc)).toBe(markdown);
  });

  it('Parse_WhenEmpty_ShouldGiveOneEmptyParagraph', () => {
    const doc = parse('', 'prose');
    expect(doc.childCount).toBe(1);
    expect(serialize(doc)).toBe('');
  });

  it('Table_WhenRaggedRows_ShouldPadAndKeepExtraColumns', () => {
    const markdown = '| A | B |\n|---|:--:|\n| 1 |\n| 1 | 2 | 3 |\n';
    const table = firstTable(markdown);

    expect(cellTexts(table)).toEqual([
      ['A', 'B', ''],
      ['1', '', ''],
      ['1', '2', '3'],
    ]);
    expect(table.attrs.align).toEqual([null, 'center', null]);
    expect(table.child(0).child(2).type.name).toBe('tableHeader');
    expect(serialize(parse(markdown, 'prose'))).toBe(markdown);
  });

  it('Table_WhenEscapedPipe_ShouldKeepItInTheCell', () => {
    const markdown = '| a \\| b | `x\\|y` |\n|---|---|\n| \\\\\\| | |\n';
    const table = firstTable(markdown);

    expect(cellTexts(table)).toEqual([
      ['a | b', 'x|y'],
      ['\\|', ''],
    ]);
    expect(serialize(parse(markdown, 'prose'))).toBe(markdown);
  });

  it('Table_WhenEdited_ShouldBecomeCanonical', () => {
    const markdown = '| Race       | Size |\n|------------|-----:|\n| Dwarf      | S    |\n';
    const doc = parse(markdown, 'prose');
    expect(serialize(doc)).toBe(markdown);

    const cell = doc.firstChild!.child(1).child(1);
    const edited = doc.type.create(
      null,
      doc.firstChild!.copy(
        doc.firstChild!.content.replaceChild(
          1,
          doc
            .firstChild!.child(1)
            .copy(
              doc
                .firstChild!.child(1)
                .content.replaceChild(
                  1,
                  cell.type.create(
                    null,
                    cell.type.schema.nodes.paragraph.create(null, doc.type.schema.text('Small')),
                  ),
                ),
            ),
        ),
      ),
    );

    expect(serialize(edited)).toBe('| Race  | Size  |\n|-------|------:|\n| Dwarf | Small |\n');
    expect(serialize(parse(serialize(edited), 'prose'))).toBe(serialize(edited));
  });

  it.each([
    ['no delimiter row', '| a | b |\n| c | d |\n'],
    ['wrong delimiter count', '| a | b |\n|---|\n'],
    ['mid paragraph', 'Text\n| a | b |\n|---|---|\n'],
  ])('Table_WhenNotATable_ShouldBeAParagraph %s', (_name, markdown) => {
    const doc = parse(markdown, 'prose');

    expect(doc.children.map((n) => n.type.name)).toEqual(['paragraph']);
    expect(serialize(doc)).toBe(markdown.replace(/^\|/, '\\|'));
  });

  it('Rule_WhenStarsOrUnderscores_ShouldBeWrittenAsDashes', () => {
    const doc = parse('One.\n\n***\n\n___\n\nTwo.\n', 'prose');

    expect(doc.children.map((n) => n.type.name)).toEqual([
      'paragraph',
      'horizontalRule',
      'horizontalRule',
      'paragraph',
    ]);
    expect(serialize(doc)).toBe('One.\n\n---\n\n---\n\nTwo.\n');
  });

  it('Paragraph_WhenItReadsAsAnotherBlock_ShouldBeEscaped', () => {
    const markdown = '\\---\n\n\\| a |\n\n\\<!-- not a comment -->\n';
    const doc = parse(markdown, 'prose');

    expect(doc.children.map((n) => n.type.name)).toEqual(['paragraph', 'paragraph', 'paragraph']);
    expect(doc.child(0).textContent).toBe('---');
    expect(serialize(doc)).toBe(markdown);
  });

  it('HtmlComment_WhenSpanningBlankLines_ShouldBeOneOpaqueBlock', () => {
    const markdown = '<!-- one\n\ntwo -->\n\nText.\n';
    const doc = parse(markdown, 'prose');

    expect(doc.children.map((n) => n.type.name)).toEqual(['opaqueBlock', 'paragraph']);
    expect(serialize(doc)).toBe(markdown);
  });

  it('Strike_WhenFlankedByWhitespace_ShouldStayText', () => {
    const doc = parse('a ~~ b ~~ c and ~~d~~ and ~one~\n', 'prose');
    const marked = doc.firstChild!.children.filter((n) =>
      n.marks.some((m) => m.type.name === 'strike'),
    );

    expect(marked.map((n) => n.text)).toEqual(['d']);
    expect(serialize(doc)).toBe('a ~~ b ~~ c and ~~d~~ and ~one~\n');
  });

  it('Strike_WhenLiteralTildesWouldPair_ShouldEscapeThem', () => {
    const doc = parse('\\~\\~a~~b~~c\n', 'prose');

    expect(doc.firstChild!.textContent).toBe('~~abc');
    expect(serialize(doc)).toBe('\\~\\~a~~b~~c\n');
  });

  it('Link_WhenSchemeIsNotAllowed_ShouldStayText', () => {
    const markdown =
      '[a](https://x.org) [b](mailto:c@d.e) [c](ftp://x.org) [d](javascript:alert)\n';
    const doc = parse(markdown, 'prose');
    const hrefs = doc
      .firstChild!.children.flatMap((n) => n.marks)
      .filter((m) => m.type.name === 'link')
      .map((m) => m.attrs.href);

    expect(hrefs).toEqual(['https://x.org', 'mailto:c@d.e']);
    expect(serialize(doc)).toBe(
      '[a](https://x.org) [b](mailto:c@d.e) \\[c\\](ftp://x.org) \\[d\\](javascript:alert)\n',
    );
  });

  it('Link_WhenInSummary_ShouldStayText', () => {
    const doc = parse('[a](https://x.org) ~~b~~', 'summary');

    expect(doc.firstChild!.children.every((n) => n.marks.length === 0)).toBe(true);
    expect(serialize(doc)).toBe('\\[a\\](https://x.org) ~~b~~');
  });

  it('RawLatexInline_WhenCodeSpanTagged_ShouldBeAnAtom', () => {
    const doc = parse('`\\DndDropCapLine{F}`{=latex}ighters `code` `x` {=latex}\n', 'prose');
    const atoms = doc.firstChild!.children.filter((n) => n.type.name === 'rawLatexInline');

    expect(atoms.map((n) => n.attrs.source)).toEqual(['\\DndDropCapLine{F}']);
    expect(serialize(doc)).toBe('`\\DndDropCapLine{F}`{=latex}ighters `code` `x` {=latex}\n');
  });

  it('List_WhenNested_ShouldKeepItemsAndMarkerWidths', () => {
    const markdown = '- a\n  - b\n    1. c\n    2. d\n- e\n\n1. f\n   - g\n';
    const doc = parse(markdown, 'prose');
    const outer = doc.firstChild!;

    expect(outer.child(0).children.map((n) => n.type.name)).toEqual(['paragraph', 'bulletList']);
    expect(serialize(doc)).toBe(markdown);
  });

  it('List_WhenItemLineLooksLikeAMarker_ShouldEscapeIt', () => {
    const markdown = '- a\n  \\- b\n';
    const doc = parse(markdown, 'prose');

    expect(doc.firstChild!.child(0).childCount).toBe(1);
    expect(serialize(doc)).toBe(markdown);
  });

  it('Locate_WhenInsideTableCells_ShouldFindMentionsAndRefsInDocumentOrder', () => {
    const doc = parse(
      `| A | B |\n|---|---|\n| [x](kb:${KB}) | [one](ref:${USE}) |\n| [y](kb:${KB}) | [two](ref:${USE}) |\n`,
      'prose',
    );
    const mentions = locate(doc, { kind: 'mention', id: KB });
    const refs = locate(doc, { kind: 'ref', id: USE });

    expect(mentions).toHaveLength(2);
    expect(mentions[0].from).toBeLessThan(mentions[1].from);
    expect(refs.map((r) => doc.textBetween(r.from, r.to))).toEqual(['one', 'two']);
  });

  it('Strike_WhenRunIsNotExactlyTwoTildes_ShouldStayText', () => {
    const markdown = 'a ~one~ ~~~three~~~ ~~two~~\n';
    const doc = parse(markdown, 'prose');
    const marked = doc.firstChild!.children.filter((n) =>
      n.marks.some((m) => m.type.name === 'strike'),
    );

    expect(marked.map((n) => n.text)).toEqual(['two']);
    expect(serialize(doc)).toBe(markdown);
  });
});
