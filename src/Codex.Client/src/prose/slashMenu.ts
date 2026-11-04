import type { Editor } from '@tiptap/react';

/** One command in the `/` menu. Lanes register their own (Write adds beats, Codex additions, chapter references…). */
export interface SlashItem {
  id: string;
  label: string;
  group: string;
  /** A disabled item is shown dimmed and never runs. A function is asked each time the menu opens or its filter changes. */
  enabled: boolean | ((editor: Editor) => boolean);
  run: (editor: Editor) => void;
}

export function isSlashItemEnabled(item: SlashItem, editor: Editor): boolean {
  return typeof item.enabled === 'function' ? item.enabled(editor) : item.enabled;
}

/** Items whose label contains the query, in registry order. */
export function filterSlashItems(items: readonly SlashItem[], query: string): SlashItem[] {
  const q = query.trim().toLowerCase();
  return items.filter((item) => q === '' || item.label.toLowerCase().includes(q));
}
