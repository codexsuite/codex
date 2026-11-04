import type { Node as PmNode } from '@tiptap/pm/model';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import { hasEntryDragData, readEntryDragData } from '../../shell/dragPayload';
import type { KbInfo, NameMatch } from './kbInfo';
import { writeTestIds } from './testIds';

export const kbMarksKey = new PluginKey('writeKbMarks');

export interface KbMarksSource {
  infos: ReadonlyMap<string, KbInfo>;
  match: (text: string) => NameMatch[];
  highlighted: string | null;
  open: (entryId: string) => void;
}

// Marks in text that do not stand for the entry's name: code, source markers and chapter links.
const skipMarks = new Set(['code', 'ref', 'chapterLink']);

function nameDecorations(doc: PmNode, source: KbMarksSource): Decoration[] {
  const decorations: Decoration[] = [];
  doc.descendants((node, pos) => {
    if (node.type.name === 'mention') {
      const entryId = node.attrs.id as string;
      const info = source.infos.get(entryId);
      decorations.push(
        Decoration.node(pos, pos + node.nodeSize, {
          'data-testid': writeTestIds.kbMention,
          'data-kb-entry': entryId,
          class: entryId === source.highlighted ? 'write-kb-mention kb-hl' : 'write-kb-mention',
          style: info ? `--cat: ${info.colour}` : '',
        }),
      );
      return false;
    }
    if (!node.isText || node.marks.some((m) => skipMarks.has(m.type.name))) return true;
    for (const match of source.match(node.text ?? '')) {
      const info = source.infos.get(match.entryId);
      decorations.push(
        Decoration.inline(pos + match.from, pos + match.to, {
          'data-testid': writeTestIds.kbAlias,
          'data-kb-entry': match.entryId,
          class: match.entryId === source.highlighted ? 'write-kb-alias kb-hl' : 'write-kb-alias',
          style: info ? `--cat: ${info.colour}` : '',
        }),
      );
    }
    return true;
  });
  return decorations;
}

/**
 * Draws the Knowledge base in the prose: each mention takes its category colour, each plain-text occurrence of an
 * entry's name or alias gets a dotted underline, and the highlighted entry's marks carry `kb-hl`. Ctrl/Cmd+click on
 * either opens the entry; an entry dropped from the sidebar becomes a mention where it lands. `source` is read on
 * every redraw, so a changed highlight or entry list only needs a transaction (meta `redraw`).
 */
export function kbMarksPlugin(source: () => KbMarksSource): Plugin {
  return new Plugin({
    key: kbMarksKey,
    props: {
      decorations: (state) => DecorationSet.create(state.doc, nameDecorations(state.doc, source())),
      handleClick(_view, _pos, event) {
        if (!(event.ctrlKey || event.metaKey) || !(event.target instanceof Element)) return false;
        const hit = event.target.closest<HTMLElement>('[data-kb-entry]');
        if (!hit?.dataset.kbEntry) return false;
        event.preventDefault();
        source().open(hit.dataset.kbEntry);
        return true;
      },
      handleDrop(view, event) {
        if (!event.dataTransfer || !hasEntryDragData(event.dataTransfer)) return false;
        const entry = readEntryDragData(event.dataTransfer);
        const at = view.posAtCoords({ left: event.clientX, top: event.clientY });
        if (!entry || !at || !view.state.doc.resolve(at.pos).parent.inlineContent) return false;
        const { mention } = view.state.schema.nodes;
        const space = view.state.schema.text(' ');
        view.dispatch(
          view.state.tr.insert(at.pos, [
            mention!.create({ id: entry.entryId, label: entry.name }),
            space,
          ]),
        );
        view.focus();
        event.preventDefault();
        return true;
      },
    },
  });
}
