import type { OutlineScene } from '@codex/core/api/structure';
import { TextField } from '~/ui';
import { planTestIds } from './testIds';
import { useSceneFieldAutosave } from './useSceneFieldAutosave';

/** The scene's summary (Markdown text), edited in place. Saves 3 s after the first edit and on blur. */
export function SceneSummaryField({
  bookId,
  scene,
  editable,
}: {
  bookId: string;
  scene: OutlineScene;
  editable: boolean;
}) {
  const { value, setValue, flush } = useSceneFieldAutosave(bookId, scene, 'summary');
  return (
    <TextField
      testID={planTestIds.sheetSummary}
      label="Summary"
      placeholder="Summarise this scene…"
      value={value}
      onChangeText={setValue}
      onBlur={flush}
      editable={editable}
      multiline
      minLines={3}
    />
  );
}
