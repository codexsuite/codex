import { Mark, Node, mergeAttributes, type Editor } from '@tiptap/react';
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import { proseTestIds } from '../testIds';
import { drawColumnAlignment, flattenPasteInCells } from './tablePlugins';

// Node and mark specs for the stored scene format (docs/prose-format.md). Pure ProseMirror/TipTap specs: no React,
// so the Markdown parser and serializer can build a schema from them without a browser.

export const DIRECTIVE_NAMES = [
  'beat',
  'kb-addition',
  'figure',
  'spell',
  'statblock',
  'pagebreak',
] as const;
export type DirectiveName = (typeof DIRECTIVE_NAMES)[number];

/** Which attribute carries a leaf directive's id on the line (`::beat{id="…"}`, `::spell{kb="…"}`), or null for none. */
export const DIRECTIVE_ATTRIBUTE: Record<DirectiveName, 'id' | 'kb' | null> = {
  beat: 'id',
  'kb-addition': 'id',
  figure: 'id',
  spell: 'kb',
  statblock: 'kb',
  pagebreak: null,
};

export const CONTAINER_NAMES = ['readaloud', 'sidebar', 'aside'] as const;

export const ProseDocument = Node.create({ name: 'doc', topNode: true, content: 'block+' });
export const SummaryDocument = Node.create({ name: 'doc', topNode: true, content: 'paragraph' });

/** `[Name](kb:<id>)`: an atom, so its label cannot be edited in place and always names one entry. */
export const Mention = Node.create({
  name: 'mention',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  addAttributes() {
    return { id: { default: '' }, label: { default: '' } };
  },
  parseHTML() {
    return [{ tag: 'span[data-mention]' }];
  },
  renderHTML({ node }) {
    return ['span', { 'data-mention': node.attrs.id, class: 'prose-mention' }, node.attrs.label];
  },
  renderText({ node }) {
    return node.attrs.label as string;
  },
});

/** `[^](ref:<use id>)`: a source marker that stands on its own after a paraphrase. */
export const RefMarker = Node.create({
  name: 'refMarker',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  addAttributes() {
    return { id: { default: '' } };
  },
  parseHTML() {
    return [{ tag: 'sup[data-ref-marker]' }];
  },
  renderHTML({ node }) {
    return ['sup', { 'data-ref-marker': node.attrs.id, class: 'prose-ref-marker' }, '^'];
  },
  renderText() {
    return '^';
  },
});

/** A leaf directive line: `::beat{id}`, `::kb-addition{id}`, `::figure{id}`, `::spell{kb}`, `::statblock{kb}`, `::pagebreak`. */
export const Directive = Node.create({
  name: 'directive',
  group: 'block',
  atom: true,
  selectable: true,
  draggable: true,
  addAttributes() {
    return { name: { default: 'pagebreak' }, ref: { default: null } };
  },
  parseHTML() {
    return [{ tag: 'div[data-directive]' }];
  },
  renderHTML({ node }) {
    return [
      'div',
      {
        'data-directive': node.attrs.name,
        'data-ref': node.attrs.ref ?? '',
        class: 'prose-directive',
      },
    ];
  },
});

/** `:::readaloud`, `:::sidebar{title="…"}` and `:::aside` blocks: ordinary blocks between the opening line and `:::`. */
export const Container = Node.create({
  name: 'container',
  group: 'block',
  content:
    '(paragraph | heading | blockquote | bulletList | orderedList | table | horizontalRule | directive | rawLatex | opaqueBlock)+',
  defining: true,
  addAttributes() {
    return { name: { default: 'aside' }, title: { default: null } };
  },
  parseHTML() {
    return [{ tag: 'div[data-container]' }];
  },
  renderHTML({ node }) {
    return ['div', { 'data-container': node.attrs.name, class: 'prose-container' }, 0];
  },
});

/** A fenced block tagged `{=latex}`, passed through to print untouched. */
export const RawLatex = Node.create({
  name: 'rawLatex',
  group: 'block',
  atom: true,
  selectable: true,
  addAttributes() {
    return { source: { default: '' } };
  },
  parseHTML() {
    return [{ tag: 'pre[data-raw-latex]', getAttrs: (el) => ({ source: el.textContent ?? '' }) }];
  },
  renderHTML({ node }) {
    return ['pre', { 'data-raw-latex': '', class: 'prose-raw-latex' }, node.attrs.source];
  },
});

/** `` `\\DndDropCapLine{F}`{=latex} ``: a raw LaTeX span inside a paragraph, passed through to print untouched. */
export const RawLatexInline = Node.create({
  name: 'rawLatexInline',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  addAttributes() {
    return { source: { default: '' } };
  },
  parseHTML() {
    return [
      { tag: 'span[data-raw-latex-inline]', getAttrs: (el) => ({ source: el.textContent ?? '' }) },
    ];
  },
  renderHTML({ node }) {
    return [
      'span',
      {
        'data-raw-latex-inline': '',
        'data-testid': proseTestIds.rawLatexInline,
        class: 'prose-raw-latex-inline',
      },
      node.attrs.source,
    ];
  },
  renderText({ node }) {
    return node.attrs.source as string;
  },
});

/**
 * A GFM pipe table. `align` has one entry per column; `source` is the text the table was read from, which the
 * serializer writes back while the content still equals what that text parses to (hand-padded columns survive).
 */
export const ProseTable = Table.extend({
  addAttributes() {
    return {
      align: {
        default: [] as (string | null)[],
        parseHTML: (el: HTMLElement): (string | null)[] => {
          try {
            const read: unknown = JSON.parse(el.getAttribute('data-align') ?? '[]');
            return Array.isArray(read) ? (read as (string | null)[]) : [];
          } catch {
            return [];
          }
        },
        renderHTML: (attributes: Record<string, unknown>) => ({
          'data-align': JSON.stringify(attributes.align),
        }),
      },
      source: { default: null as string | null },
    };
  },
  addProseMirrorPlugins() {
    return [...(this.parent?.() ?? []), flattenPasteInCells(), drawColumnAlignment()];
  },
}).configure({ HTMLAttributes: { 'data-testid': proseTestIds.table } });

export const ProseTableRow = TableRow;

// In a cell, Enter moves to the next one (the last stays put) and Shift-Enter does nothing: no line breaks in a cell.
const inCell = (editor: Editor) => editor.isActive('tableCell') || editor.isActive('tableHeader');
const cellKeys = {
  Enter: ({ editor }: { editor: Editor }) =>
    inCell(editor) && (editor.commands.goToNextCell() || true),
  'Shift-Enter': ({ editor }: { editor: Editor }) => inCell(editor),
};

/** A cell holds exactly one paragraph of inline content, so every table can go back to a pipe table. */
export const ProseTableHeader = TableHeader.extend({
  content: 'paragraph',

  addKeyboardShortcuts: () => cellKeys,
}).configure({ HTMLAttributes: { 'data-testid': proseTestIds.tableHeaderCell } });
export const ProseTableCell = TableCell.extend({
  content: 'paragraph',
  addKeyboardShortcuts: () => cellKeys,
});

/** Anything the parser does not understand (unknown directives and containers, other fences), kept verbatim. */
export const OpaqueBlock = Node.create({
  name: 'opaqueBlock',
  group: 'block',
  atom: true,
  selectable: true,
  addAttributes() {
    return { raw: { default: '' } };
  },
  parseHTML() {
    return [{ tag: 'pre[data-opaque]' }];
  },
  renderHTML({ node }) {
    return ['pre', { 'data-opaque': '', class: 'prose-opaque' }, node.attrs.raw];
  },
});

export const Strong = Mark.create({
  name: 'bold',
  parseHTML() {
    return [{ tag: 'strong' }, { tag: 'b' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ['strong', HTMLAttributes, 0];
  },
  addKeyboardShortcuts() {
    return { 'Mod-b': () => this.editor.commands.toggleMark(this.name) };
  },
});

export const Emphasis = Mark.create({
  name: 'italic',
  parseHTML() {
    return [{ tag: 'em' }, { tag: 'i' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ['em', HTMLAttributes, 0];
  },
  addKeyboardShortcuts() {
    return { 'Mod-i': () => this.editor.commands.toggleMark(this.name) };
  },
});

/** `~~x~~`. */
export const Strikethrough = Mark.create({
  name: 'strike',
  parseHTML() {
    return [{ tag: 's' }, { tag: 'del' }, { tag: 'strike' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ['s', HTMLAttributes, 0];
  },
  addKeyboardShortcuts() {
    return { 'Mod-Shift-x': () => this.editor.commands.toggleMark(this.name) };
  },
});

/** Unlike the stock code mark this one coexists with other marks, because `**`x`**` is legal Markdown. */
export const InlineCode = Mark.create({
  name: 'code',
  excludes: '',
  code: true,
  parseHTML() {
    return [{ tag: 'code' }];
  },
  renderHTML({ HTMLAttributes }) {
    return ['code', HTMLAttributes, 0];
  },
  addKeyboardShortcuts() {
    return { 'Mod-e': () => this.editor.commands.toggleMark(this.name) };
  },
});

function idMark(name: string, scheme: string, tag: string, className: string) {
  return Mark.create({
    name,
    inclusive: false,
    addAttributes() {
      return { id: { default: '' } };
    },
    parseHTML() {
      return [{ tag: `${tag}[data-${scheme}]` }];
    },
    renderHTML({ HTMLAttributes, mark }) {
      return [
        tag,
        mergeAttributes(HTMLAttributes, { [`data-${scheme}`]: mark.attrs.id, class: className }),
        0,
      ];
    },
  });
}

/** `[passage](ref:<use id>)`: the quoted or paraphrased words a source marker covers. May span other marks. */
export const RefMark = idMark('ref', 'ref', 'span', 'prose-ref');

/** `[Title](chapter:<chapter id>)`: a chapter reference whose number is worked out from the id. */
export const ChapterLinkMark = idMark('chapterLink', 'chapter', 'span', 'prose-chapter-link');
