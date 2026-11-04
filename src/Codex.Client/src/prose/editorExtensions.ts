import { Placeholder } from '@tiptap/extension-placeholder';
import { ReactNodeViewRenderer, type AnyExtension, type Editor } from '@tiptap/react';
import { filterEntries, type MentionEntry } from './entries';
import { filterSlashItems, isSlashItemEnabled, type SlashItem } from './slashMenu';
import { proseExtensions, summaryExtensions } from './schema/extensions';
import { Container, Directive, OpaqueBlock, RawLatex, RawLatexInline } from './schema/nodes';
import { createSuggestion } from './suggestion/createSuggestion';
import type { PopupController } from './suggestion/usePopup';
import { ContainerView } from './views/ContainerView';
import { DirectiveView } from './views/DirectiveView';
import { RawLatexInlineView } from './views/RawLatexInlineView';
import { RawLatexView } from './views/RawLatexView';
import { SourceBlockView } from './views/SourceBlockView';

export interface LiveSources {
  entries: () => readonly MentionEntry[];
  slashItems: () => readonly SlashItem[];
}

export interface Controllers {
  mention: PopupController<MentionEntry>;
  slash?: PopupController<SlashItem>;
}

function mentionSuggestion(sources: LiveSources, controller: PopupController<MentionEntry>) {
  return createSuggestion<MentionEntry>({
    name: 'mentionSuggestion',
    char: '@',
    items: (query) => filterEntries(sources.entries(), query),
    controller,
    onSelect: (editor, range, entry) => {
      editor
        .chain()
        .focus()
        .insertContentAt(range, [
          { type: 'mention', attrs: { id: entry.id, label: entry.name } },
          { type: 'text', text: ' ' },
        ])
        .run();
    },
  });
}

function slashSuggestion(sources: LiveSources, controller: PopupController<SlashItem>) {
  return createSuggestion<SlashItem>({
    name: 'slashSuggestion',
    char: '/',
    items: (query) => filterSlashItems(sources.slashItems(), query),
    controller,
    onSelect: (editor: Editor, range, item) => {
      if (!isSlashItemEnabled(item, editor)) return;
      editor.chain().focus().deleteRange(range).run();
      item.run(editor);
    },
  });
}

export function proseEditorExtensions(
  sources: LiveSources,
  controllers: Required<Controllers>,
  placeholder?: string,
): AnyExtension[] {
  const cards: Record<string, AnyExtension> = {
    directive: Directive.extend({ addNodeView: () => ReactNodeViewRenderer(DirectiveView) }),
    container: Container.extend({ addNodeView: () => ReactNodeViewRenderer(ContainerView) }),
    rawLatex: RawLatex.extend({
      addNodeView: () =>
        ReactNodeViewRenderer(RawLatexView, { stopEvent: () => true, ignoreMutation: () => true }),
    }),
    rawLatexInline: RawLatexInline.extend({
      addNodeView: () => ReactNodeViewRenderer(RawLatexInlineView, { as: 'span' }),
    }),
    opaqueBlock: OpaqueBlock.extend({ addNodeView: () => ReactNodeViewRenderer(SourceBlockView) }),
  };
  return [
    ...proseExtensions().map((ext) => cards[ext.name] ?? ext),
    mentionSuggestion(sources, controllers.mention),
    slashSuggestion(sources, controllers.slash),
    ...(placeholder ? [Placeholder.configure({ placeholder })] : []),
  ];
}

export function summaryEditorExtensions(
  sources: LiveSources,
  controller: PopupController<MentionEntry>,
  placeholder?: string,
): AnyExtension[] {
  return [
    ...summaryExtensions(),
    mentionSuggestion(sources, controller),
    ...(placeholder ? [Placeholder.configure({ placeholder })] : []),
  ];
}
