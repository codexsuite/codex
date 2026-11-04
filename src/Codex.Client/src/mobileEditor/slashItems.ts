import type { Editor } from '@tiptap/react';
import { richBlockSlashItems } from '../prose/richSlashItems';
import type { SlashItem } from '../prose/slashMenu';
import type { BlockActions } from './runCommand';
import { triggerText } from './triggerText';

/** Inserts `/` or `@` at the cursor (after a space when it follows a word) so its suggestion menu opens. */
export function insertTrigger(editor: Editor, trigger: '/' | '@'): void {
  const { $from } = editor.state.selection;
  const before = $from.parent.textBetween(Math.max(0, $from.parentOffset - 1), $from.parentOffset);
  editor.chain().focus().insertContent(triggerText(before, trigger)).run();
}

// The `/` menu on a phone: what needs no picker and no AI. TODO: Codex addition, Chapter reference, Source reference,
// Figure and Continue writing join when their pickers exist on mobile (Beat is read-only in M2).
export function mobileSlashItems(actions: BlockActions): SlashItem[] {
  return [
    {
      id: 'scene-beat',
      label: 'Scene beat',
      group: 'Insert',
      enabled: true,
      run: actions.beat,
    },
    {
      id: 'mention-entry',
      label: 'Mention entry',
      group: 'Codex',
      enabled: true,
      run: (editor) => insertTrigger(editor, '@'),
    },
    {
      id: 'new-scene-from-here',
      label: 'Split scene here',
      group: 'Insert',
      enabled: true,
      run: actions.newScene,
    },
    {
      id: 'block-quote',
      label: 'Block quote',
      group: 'Formatting',
      enabled: true,
      run: (editor) => void editor.chain().focus().toggleBlockquote().run(),
    },
    ...richBlockSlashItems(),
  ];
}
