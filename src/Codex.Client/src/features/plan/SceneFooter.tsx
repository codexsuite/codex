import type { OutlineScene } from '@codex/core/api/structure';
import { wordsText } from '@codex/core/features/plan/format';
import { SceneEntryAdd } from './SceneEntryAdd';
import { SceneLabelAdd } from './SceneLabelAdd';
import { planTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

// The card's last row: `+ Codex` and `Label` on the left, the words written in Write on the right.
export function SceneFooter({ scene }: { scene: OutlineScene }) {
  const canWrite = useCanWrite();
  return (
    <footer className="flex items-center gap-0.5 px-1 pb-1">
      {canWrite && (
        <>
          <SceneEntryAdd scene={scene} />
          <SceneLabelAdd scene={scene} />
        </>
      )}
      <span className="flex-1" />
      <span
        data-testid={planTestIds.sceneWords(scene.id)}
        className={`pr-1 text-2xs whitespace-nowrap text-muted tabular-nums ${scene.words > 0 ? '' : 'italic'}`}
      >
        {scene.words > 0 ? wordsText(scene.words) : 'Not written'}
      </span>
    </footer>
  );
}
