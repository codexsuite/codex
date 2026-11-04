import type { OutlineScene } from '@codex/core/api/structure';
import { TextField } from '~/ui';
import { planTestIds } from './testIds';
import { useSceneFieldAutosave } from './useSceneFieldAutosave';

/** The scene's title, edited in place; empty is Untitled. Saves 3 s after the first edit and on blur. */
export function SceneTitleField({
  bookId,
  scene,
  editable,
}: {
  bookId: string;
  scene: OutlineScene;
  editable: boolean;
}) {
  const { value, setValue, flush } = useSceneFieldAutosave(bookId, scene, 'title');
  return (
    <TextField
      testID={planTestIds.sheetTitle}
      label="Title"
      placeholder="Untitled"
      value={value}
      onChangeText={setValue}
      onBlur={flush}
      editable={editable}
      returnKeyType="done"
    />
  );
}
