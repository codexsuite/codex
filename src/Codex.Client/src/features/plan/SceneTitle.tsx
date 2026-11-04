import type { OutlineScene } from '@codex/core/api/structure';
import { planTestIds } from './testIds';
import { useSceneField } from './useSceneField';
import { useCanWrite } from '../../shell/bookAccess';

// The scene's subtitle, edited in place.
export function SceneTitle({ scene }: { scene: OutlineScene }) {
  const { value, setValue, flush } = useSceneField(scene, 'title');
  const canWrite = useCanWrite();
  return (
    <input
      data-testid={planTestIds.sceneTitle(scene.id)}
      aria-label={`Title of scene ${scene.number}`}
      placeholder="Untitled"
      readOnly={!canWrite}
      value={value}
      onChange={(event) => setValue(event.target.value)}
      onBlur={flush}
      className="w-full min-w-0 rounded-sm border-0 bg-transparent px-0 py-0 text-sm font-medium text-text placeholder:text-muted"
    />
  );
}
