import { Fragment, Slice } from '@tiptap/pm/model';
import { Plugin } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';

const CELLS = new Set(['tableCell', 'tableHeader']);

/** Pasted content inside a cell becomes one run of plain text: a cell holds one paragraph, never blocks or breaks. */
export function flattenPasteInCells(): Plugin {
  return new Plugin({
    props: {
      transformPasted(slice, view) {
        const { $from } = view.state.selection;
        let inCell = false;
        for (let d = $from.depth; d > 0 && !inCell; d--)
          inCell = CELLS.has($from.node(d).type.name);
        if (!inCell) return slice;
        const text = slice.content
          .textBetween(0, slice.content.size, ' ', ' ')
          .replace(/\s+/g, ' ');
        return new Slice(
          text === '' ? Fragment.empty : Fragment.from(view.state.schema.text(text)),
          0,
          0,
        );
      },
    },
  });
}

/** Draws each column's `align` on its cells, so a column set to centre or right looks that way while editing. */
export function drawColumnAlignment(): Plugin {
  return new Plugin({
    props: {
      decorations(state) {
        const decorations: Decoration[] = [];
        state.doc.descendants((node, pos) => {
          if (node.type.name !== 'table') return true;
          const align = node.attrs.align as (string | null)[];
          node.forEach((row, rowOffset) => {
            row.forEach((cell, cellOffset, column) => {
              const side = align[column];
              if (side) {
                const from = pos + 1 + rowOffset + 1 + cellOffset;
                decorations.push(
                  Decoration.node(from, from + cell.nodeSize, { style: `text-align: ${side}` }),
                );
              }
            });
          });
          return false;
        });
        return DecorationSet.create(state.doc, decorations);
      },
    },
  });
}
