import { useDroppable } from '@dnd-kit/core';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import type { OutlineChapter } from '@codex/core/api/structure';
import { ChapterMenu } from './ChapterMenu';
import { ChapterTitle } from './ChapterTitle';
import { DragGrip } from './DragGrip';
import { emphasisOf } from '@codex/core/features/plan/emphasis';
import { compactWords, plural, sceneWords } from '@codex/core/features/plan/format';
import { PlusIcon } from './icons';
import type { PlanFilter } from '@codex/core/features/plan/planFilter';
import { OpenerDialog } from './OpenerDialog';
import { OpenerThumb } from './OpenerThumb';
import { SceneCard } from './SceneCard';
import { planTestIds } from './testIds';
import { useCreate } from './useCreate';
import { useCanWrite } from '../../shell/bookAccess';

// A chapter column: its number, scene count and words over its title, its opener art and ⋯ (Rename, Opener art…,
// Delete), a rule, then its scene cards in order.
export function ChapterColumn({
  chapter,
  highlightedEntry,
  filter,
}: {
  chapter: OutlineChapter;
  highlightedEntry: string | null;
  filter: PlanFilter;
}) {
  const { bookId = '' } = useParams();
  const [openerOpen, setOpenerOpen] = useState(false);
  const { createScene } = useCreate();
  const [renaming, setRenaming] = useState(false);
  // Below write (#80): no rename, menu, opener change or New scene.
  const canWrite = useCanWrite();
  const overline = chapter.number === null ? 'Unnumbered' : `Chapter ${chapter.number}`;
  const name = `${overline}: ${chapter.title}`;
  const words = sceneWords(chapter.scenes);
  const { setNodeRef } = useDroppable({
    id: `scenes:${chapter.id}`,
    data: { kind: 'scene', chapterId: chapter.id },
  });
  return (
    <section
      ref={setNodeRef}
      id={`chapter-${chapter.id}`}
      data-testid={planTestIds.chapter(chapter.id)}
      className="flex w-[var(--column-width)] max-w-full min-w-0 shrink-0 flex-col rounded-md border border-border bg-surface-2"
    >
      <header className="group flex items-start gap-0.5 border-b border-border py-1.5 pr-1 pl-0.5">
        <DragGrip kind="chapter" id={chapter.id} label={chapter.title} />
        <div data-testid={planTestIds.chapterHeading} className="min-w-0 flex-1 pt-px">
          <div className="flex gap-1.5 text-2xs font-medium tracking-[.02em] text-muted">
            <span className="uppercase">{overline}</span>{' '}
            <span>
              · {plural(chapter.scenes.length, 'scene')}
              {words > 0 && ` · ${compactWords(words)}`}
            </span>
          </div>
          <ChapterTitle
            chapter={chapter}
            editing={renaming}
            onEdit={() => canWrite && setRenaming(true)}
            onDone={() => setRenaming(false)}
          />
        </div>
        <OpenerThumb
          opener={chapter.opener}
          name={name}
          testId={planTestIds.chapterOpenerThumb(chapter.id)}
          onClick={canWrite ? () => setOpenerOpen(true) : undefined}
        />
        {canWrite && (
          <ChapterMenu
            chapter={chapter}
            name={name}
            onRename={() => setRenaming(true)}
            onOpener={() => setOpenerOpen(true)}
          />
        )}
      </header>
      {openerOpen && (
        <OpenerDialog
          bookId={bookId}
          kind="chapter"
          id={chapter.id}
          name={name}
          opener={chapter.opener}
          onClose={() => setOpenerOpen(false)}
        />
      )}
      <div className="flex min-h-10 flex-col gap-1.5 p-1.5">
        {chapter.scenes.length === 0 && (
          <p
            data-testid={planTestIds.chapterEmpty(chapter.id)}
            className="m-0 px-1 py-2 text-center text-sm text-muted"
          >
            No scenes
          </p>
        )}
        {chapter.scenes.map((scene, index) => (
          <SceneCard
            key={scene.id}
            scene={scene}
            position={index + 1}
            emphasis={emphasisOf(scene, highlightedEntry, filter)}
          />
        ))}
      </div>
      {canWrite && (
        <button
          type="button"
          data-testid={planTestIds.newScene(chapter.id)}
          onClick={() => void createScene(chapter.id)}
          className="mx-1.5 mb-1.5 inline-flex h-[var(--control-height)] cursor-pointer items-center justify-center gap-[5px] rounded-sm border-0 bg-transparent text-sm font-medium text-muted hover:bg-surface hover:text-text focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-3"
        >
          <PlusIcon />
          New scene
        </button>
      )}
    </section>
  );
}
