import type { OutlineScene } from '@codex/core/api/structure';
import { useDndContext } from '@dnd-kit/core';
import { DragGrip } from './DragGrip';
import { EntryChip } from './EntryChip';
import type { Emphasis } from '@codex/core/features/plan/emphasis';
import { LabelPill } from './LabelPill';
import { SceneFooter } from './SceneFooter';
import { SceneMenu } from './SceneMenu';
import { SceneSummary } from './SceneSummary';
import { SceneTitle } from './SceneTitle';
import { planTestIds } from './testIds';
import { useEntryDrop } from './useEntryDrop';
import { useCanWrite } from '../../shell/bookAccess';

const emphasisClass: Record<Emphasis, string> = {
  none: '',
  hit: 'ring-2 ring-accent',
  dim: 'opacity-30',
};

// One scene on the board: a header strip (grip, its number in the chapter, labels), then the title and summary,
// the linked entries, and a footer with `+ Codex`, `Label` and the words already written.
export function SceneCard({
  scene,
  position,
  emphasis,
}: {
  scene: OutlineScene;
  /** 1-based place in its chapter: the card reads `Scene 2`, the API's `scene.number` (`1.2`) names it elsewhere. */
  position: number;
  emphasis: Emphasis;
}) {
  const lifted = useDndContext().active?.id === `scene:${scene.id}`;
  const drop = useEntryDrop(scene.id);
  const canWrite = useCanWrite();
  return (
    <article
      id={`scene-${scene.id}`}
      data-testid={planTestIds.scene(scene.id)}
      data-emphasis={emphasis}
      {...(canWrite ? drop.handlers : {})}
      className={`group rounded-sm border border-border bg-surface shadow-raised transition-opacity ${emphasisClass[emphasis]} ${lifted ? 'opacity-40' : ''} ${drop.over ? 'outline-2 outline-accent' : ''}`}
    >
      <header className="flex min-w-0 items-center gap-0.5 border-b border-border py-[3px] pr-[3px] pl-px">
        <DragGrip kind="scene" id={scene.id} label={`Scene ${scene.number}`} />
        <span
          data-testid={planTestIds.sceneNumber}
          className="mr-1 text-xs font-semibold whitespace-nowrap text-text"
        >
          Scene {position}
        </span>
        {scene.hasPendingDraft && (
          <span
            data-testid={planTestIds.scenePending(scene.id)}
            className="rounded-full bg-accent-soft px-1.5 py-px text-2xs whitespace-nowrap text-accent"
          >
            Draft pending
          </span>
        )}
        <span className="flex min-w-0 flex-1 gap-1 overflow-hidden">
          {scene.labels.map((label) => (
            <LabelPill key={label.id} sceneId={scene.id} label={label} />
          ))}
        </span>
        <SceneMenu scene={scene} />
      </header>
      <div className="flex min-h-[52px] flex-col gap-1 px-2 py-1.5">
        <SceneTitle scene={scene} />
        <SceneSummary scene={scene} />
      </div>
      {scene.linkedEntries.length > 0 && (
        <div className="flex flex-wrap gap-1 px-2 pb-1">
          {scene.linkedEntries.map((entry) => (
            <EntryChip key={entry.id} sceneId={scene.id} entry={entry} />
          ))}
        </div>
      )}
      <SceneFooter scene={scene} />
    </article>
  );
}
