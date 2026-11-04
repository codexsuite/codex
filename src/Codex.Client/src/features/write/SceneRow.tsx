import { useRef } from 'react';
import type { ChapterLinkLabel, SceneView } from '@codex/core/features/write/manuscriptView';
import { SceneGutter } from './SceneGutter';
import { SceneProse } from './SceneProse';
import { writeTestIds } from './testIds';
import { useBeatDrop } from './useBeatDrop';

interface SceneRowProps {
  view: SceneView;
  chapterLinks: ReadonlyMap<string, ChapterLinkLabel>;
}

// One scene: the prose column and its gutter. `data-scene-id` is how scrolling and the minimap find it.
export function SceneRow({ view, chapterLinks }: SceneRowProps) {
  const { scene } = view;
  const row = useRef<HTMLElement>(null);
  const dropLine = useBeatDrop(scene.id, row);
  return (
    <section
      data-testid={writeTestIds.scene(scene.id)}
      data-scene-id={scene.id}
      data-scene-number={scene.number}
      className="write-row write-scene relative"
      ref={row}
    >
      {dropLine !== null && (
        <div
          className="write-drop-line"
          data-testid={writeTestIds.dropLine}
          style={{ top: dropLine }}
        >
          <span>{view.heading}</span>
        </div>
      )}
      <div className="write-scene-main min-w-0">
        <SceneProse
          sceneId={scene.id}
          body={view.body}
          version={view.version}
          chapterLinks={chapterLinks}
        />
      </div>
      <SceneGutter view={view} />
    </section>
  );
}
