import { Extension, type Editor } from '@tiptap/react';
import { PluginKey } from '@tiptap/pm/state';
import { Suggestion } from '@tiptap/suggestion';
import type { PopupController } from './usePopup';

interface SuggestionConfig<T> {
  name: string;
  char: string;
  items: (query: string, editor: Editor) => T[];
  onSelect: (editor: Editor, range: { from: number; to: number }, item: T) => void;
  controller: PopupController<T>;
}

/** A TipTap extension that opens a popup (driven by `controller`) when `char` is typed and hands the chosen item to `onSelect`. */
export function createSuggestion<T>(config: SuggestionConfig<T>) {
  return Extension.create({
    name: config.name,
    addProseMirrorPlugins() {
      return [
        Suggestion<T, T>({
          editor: this.editor,
          char: config.char,
          pluginKey: new PluginKey(config.name),
          items: ({ query, editor }) => config.items(query, editor),
          command: ({ editor, range, props }) => config.onSelect(editor, range, props),
          render: () => ({
            onStart: config.controller.onStart,
            onUpdate: config.controller.onUpdate,
            onExit: config.controller.onExit,
            onKeyDown: config.controller.onKeyDown,
          }),
        }),
      ];
    },
  });
}
