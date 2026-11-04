import { Fragment } from 'react';
import { ChapterHeader } from './ChapterHeader';
import type { ChapterLinkLabel, ChapterView } from '@codex/core/features/write/manuscriptView';
import { SceneDivider } from './SceneDivider';
import { SceneRow } from './SceneRow';
import { writeTestIds } from './testIds';

interface ChapterSectionProps {
  view: ChapterView;
  chapterLinks: ReadonlyMap<string, ChapterLinkLabel>;
  onShowOnly: (chapterId: string) => void;
}

export function ChapterSection({ view, chapterLinks, onShowOnly }: ChapterSectionProps) {
  return (
    <section data-testid={writeTestIds.chapter(view.chapter.id)} className="write-chapter">
      <ChapterHeader view={view} onShowOnly={onShowOnly} />
      {view.scenes.map((scene, index) => (
        <Fragment key={scene.scene.id}>
          <SceneRow view={scene} chapterLinks={chapterLinks} />
          {index < view.scenes.length - 1 && <SceneDivider sceneId={scene.scene.id} />}
        </Fragment>
      ))}
    </section>
  );
}
