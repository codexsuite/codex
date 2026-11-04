import type { Editor } from '@tiptap/react';
import { richBlockSlashItems, type SlashItem } from '../../prose';
import { citeTarget, releaseKeptSelection } from './citeSelection';
import type { BlockOps } from './useBlockOps';

interface Handlers {
  sceneId: string;
  ops: BlockOps;
  openChapterPicker: (editor: Editor) => void;
  openAdditionPicker: (editor: Editor) => void;
  openSourcePicker: (editor: Editor) => void;
  openFigureDialog: (editor: Editor) => void;
  continueWriting: () => void;
}

// Every item but Source reference, which cites the words `/` was typed over, lets go of them first.
const plain = (item: SlashItem): SlashItem => ({
  ...item,
  run: (editor) => {
    releaseKeptSelection(editor.view);
    item.run(editor);
  },
});

// Write's `/` menu. An item with nothing to work on is listed, dimmed, and never runs.
export function writeSlashItems({
  sceneId,
  ops,
  openChapterPicker,
  openAdditionPicker,
  openSourcePicker,
  openFigureDialog,
  continueWriting,
}: Handlers): SlashItem[] {
  const items: SlashItem[] = [
    {
      id: 'scene-beat',
      label: 'Scene beat',
      group: 'AI',
      enabled: true,
      run: (editor) => void ops.insertBeat(sceneId, editor),
    },
    {
      id: 'continue-writing',
      label: 'Continue writing',
      group: 'AI',
      enabled: true,
      run: continueWriting,
    },
    {
      id: 'codex-addition',
      label: 'Codex addition',
      group: 'Codex',
      enabled: true,
      run: openAdditionPicker,
    },
    {
      id: 'mention-entry',
      label: 'Mention entry',
      group: 'Codex',
      enabled: true,
      run: (editor) => void editor.chain().focus().insertContent('@').run(),
    },
    {
      id: 'chapter-reference',
      label: 'Chapter reference',
      group: 'Insert',
      enabled: true,
      run: openChapterPicker,
    },
    {
      id: 'source-reference',
      label: 'Source reference',
      group: 'Insert',
      // It cites the selection `/` was typed over, else the sentence before the caret; dimmed when there is neither.
      enabled: (editor) => citeTarget(editor.state) !== null,
      run: openSourcePicker,
    },
    {
      id: 'figure',
      label: 'Figure…',
      group: 'Insert',
      enabled: true,
      run: openFigureDialog,
    },
    {
      id: 'new-scene-from-here',
      label: 'Split scene here',
      group: 'Insert',
      enabled: true,
      run: (editor) => void ops.splitScene(sceneId, editor),
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
  return items.map((item) => (item.id === 'source-reference' ? item : plain(item)));
}
