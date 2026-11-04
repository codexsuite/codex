import { DOMParser as PMDOMParser, DOMSerializer, type Node as PMNode } from '@tiptap/pm/model';
import { describe, expect, it } from 'vitest';
import { proseSchema } from './schemas';

// What a copy then paste does to a node: out through the node's renderHTML, back in through parseHTML.
function viaClipboard(doc: PMNode): PMNode {
  const holder = document.createElement('div');
  holder.append(DOMSerializer.fromSchema(proseSchema).serializeFragment(doc.content));
  return PMDOMParser.fromSchema(proseSchema).parse(holder);
}

describe('prose schema clipboard round trip', () => {
  const { nodes } = proseSchema;
  const doc = (...blocks: PMNode[]) => proseSchema.topNodeType.create(null, blocks);

  it('RawLatexInline_WhenCopiedAndPasted_ShouldKeepItsSource', () => {
    const chip = nodes.rawLatexInline.create({ source: '\\DndDropCapLine{F}' });
    const pasted = viaClipboard(doc(nodes.paragraph.create(null, [chip])));

    expect(pasted.firstChild!.firstChild!.type.name).toBe('rawLatexInline');
    expect(pasted.firstChild!.firstChild!.attrs.source).toBe('\\DndDropCapLine{F}');
  });

  it('RawLatex_WhenCopiedAndPasted_ShouldKeepItsSource', () => {
    const block = nodes.rawLatex.create({ source: '\\begin{x}\n  y\n\\end{x}' });
    const pasted = viaClipboard(doc(block));

    expect(pasted.firstChild!.type.name).toBe('rawLatex');
    expect(pasted.firstChild!.attrs.source).toBe('\\begin{x}\n  y\n\\end{x}');
  });

  it('Table_WhenCopiedAndPasted_ShouldKeepColumnAlignmentAsAnArray', () => {
    const cell = (type: 'tableHeader' | 'tableCell') =>
      nodes[type].create(null, nodes.paragraph.create());
    const table = nodes.table.create({ align: ['left', null, 'right'], source: null }, [
      nodes.tableRow.create(null, [cell('tableHeader'), cell('tableHeader'), cell('tableHeader')]),
      nodes.tableRow.create(null, [cell('tableCell'), cell('tableCell'), cell('tableCell')]),
    ]);
    const pasted = viaClipboard(doc(table));

    expect(pasted.firstChild!.type.name).toBe('table');
    expect(pasted.firstChild!.attrs.align).toEqual(['left', null, 'right']);
  });
});
