import type { Editor } from '@tiptap/react';
import { TextSelection } from '@tiptap/pm/state';
import type { SlashItem } from './slashMenu';
import { newTable } from './tables/tableCommands';

/** `/` items for blocks that any prose surface can hold: a table (header row plus two rows of two cells) and a divider. */
export function richBlockSlashItems(): SlashItem[] {
  return [
    {
      id: 'table',
      label: 'Table',
      group: 'Insert',
      enabled: (editor) => !editor.isActive('table'),
      run: (editor) => {
        const from = editor.state.selection.from;
        editor.chain().focus().insertContent(newTable(3, 2)).run();
        const { doc, tr } = editor.state;
        let at = -1;
        doc.nodesBetween(Math.max(0, from - 1), doc.content.size, (node, pos) => {
          if (at < 0 && node.type.name === 'table') at = pos;
          return at < 0;
        });
        if (at < 0) return;
        // A table at the very end leaves the caret nowhere to go on to: give the document a paragraph after it.
        if (doc.lastChild?.type.name === 'table')
          tr.insert(doc.content.size, doc.type.schema.nodes.paragraph.create());
        editor.view.dispatch(tr.setSelection(TextSelection.near(tr.doc.resolve(at + 1))));
      },
    },
    {
      id: 'divider',
      label: 'Divider',
      group: 'Insert',
      enabled: (editor) => !editor.isActive('table'),
      run: insertDivider,
    },
  ];
}

// A rule replaces the empty line `/` was typed on, or goes after the block the line belongs to; the caret ends below it.
function insertDivider(editor: Editor): void {
  const { state, view } = editor;
  const { $from } = state.selection;
  const { horizontalRule, paragraph } = state.schema.nodes;
  const blank = $from.parent.type === paragraph && $from.parent.childCount === 0;
  const from = blank ? $from.before() : $from.after();
  const to = blank ? $from.after() : from;
  const tr = state.tr.replaceWith(from, to, [horizontalRule.create(), paragraph.create()]);
  view.dispatch(tr.setSelection(TextSelection.near(tr.doc.resolve(from + 2))).scrollIntoView());
  view.focus();
}
