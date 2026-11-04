import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import { MISSING_CHAPTER, type ChapterLinkLabel } from '@codex/core/features/write/manuscriptView';
import { writeTestIds } from './testIds';

export const chapterLinkKey = new PluginKey('writeChapterLinks');

function labelElement(
  chapterId: string,
  label: ChapterLinkLabel,
  open: (chapterId: string) => void,
): HTMLElement {
  const element = document.createElement('span');
  element.className = label.missing
    ? 'write-chapter-ref write-chapter-ref-missing'
    : label.archived
      ? 'write-chapter-ref write-chapter-ref-archived'
      : 'write-chapter-ref';
  if (label.archived) {
    element.title = 'This chapter is archived';
    element.dataset.archived = 'true';
    element.role = 'link';
    element.tabIndex = 0;
    element.addEventListener('click', () => open(chapterId));
    element.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') open(chapterId);
    });
  } else if (label.title) {
    element.title = label.title;
  }
  element.dataset.testid = writeTestIds.chapterRef;
  element.dataset.chapter = chapterId;
  element.dataset.missing = String(label.missing);
  element.textContent = label.text;
  return element;
}

/**
 * Draws each `[Title](chapter:<id>)` link as its live label (`§ Chapter 5`, or `Missing chapter`) instead of the
 * title stored in the prose (an archived chapter shows its title and `open`s Plan's Archived list). The stored Markdown is untouched: the link text is hidden and a widget stands in.
 * `labels` is read on every redraw, so a renumbered outline only needs a transaction to show the new numbers.
 */
export function chapterLinkPlugin(
  labels: () => ReadonlyMap<string, ChapterLinkLabel>,
  open: (chapterId: string) => void,
): Plugin {
  return new Plugin({
    key: chapterLinkKey,
    props: {
      decorations(state) {
        const decorations: Decoration[] = [];
        const known = labels();
        // One widget per run of adjacent text nodes carrying the same link (a bold word inside it splits the nodes).
        const run = { id: '', end: -1 };
        state.doc.descendants((node, pos) => {
          if (!node.isText) return;
          const mark = node.marks.find((m) => m.type.name === 'chapterLink');
          if (!mark) return;
          const id = mark.attrs.id as string;
          const label = known.get(id) ?? MISSING_CHAPTER;
          decorations.push(
            Decoration.inline(pos, pos + node.nodeSize, { class: 'write-chapter-link-source' }),
          );
          if (!(run.id === id && run.end === pos)) {
            decorations.push(
              Decoration.widget(pos, () => labelElement(id, label, open), {
                side: -1,
                key: `${id}:${label.text}:${label.archived ?? false}:${pos}`,
              }),
            );
          }
          run.id = id;
          run.end = pos + node.nodeSize;
        });
        return DecorationSet.create(state.doc, decorations);
      },
    },
  });
}
