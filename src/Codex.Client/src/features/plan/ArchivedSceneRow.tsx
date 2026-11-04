import { useRef, useState } from 'react';
import type { ArchivedScene } from '@codex/core/api/plan';
import type { OutlineChapter } from '@codex/core/api/structure';
import { plainSummary, plural } from '@codex/core/features/plan/format';
import { planTestIds } from './testIds';
import { useChapterOps } from './useChapterOps';
import { useSceneArchive } from './useSceneArchive';
import { useCanWrite } from '../../shell/bookAccess';

const buttonClass =
  'h-[22px] shrink-0 cursor-pointer rounded-xs border border-border bg-transparent px-1.5 text-xs font-medium text-text hover:bg-surface-2';

// One archived scene: where it was, a line of its summary, the source uses archived with it, and Restore. When its chapter
// is archived too, Restore asks first: Restore both brings the chapter back and then the scene; Cancel leaves it archived.
export function ArchivedSceneRow({
  scene,
  chapter,
}: {
  scene: ArchivedScene;
  chapter: OutlineChapter;
}) {
  const { restore } = useSceneArchive();
  // Below write (#80) archived scenes list, but are not restored.
  const canWrite = useCanWrite();
  const { restoreQuietly } = useChapterOps();
  const [asking, setAsking] = useState(false);
  const restoreButton = useRef<HTMLButtonElement>(null);
  const summary = plainSummary(scene.summary);

  const close = () => {
    setAsking(false);
    restoreButton.current?.focus();
  };
  const restoreBoth = async () => {
    await restoreQuietly(chapter.id);
    setAsking(false);
    await restore(scene.id);
  };

  return (
    <div
      data-testid={planTestIds.archivedScene(scene.id)}
      className="flex flex-col gap-1 rounded-sm border-b border-dashed border-border px-2 py-1.5 text-sm last:border-b-0"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col">
          <span className="text-2xs tracking-[.02em] text-muted uppercase">
            {scene.chapterNumber === null ? 'Was' : `Chapter ${scene.chapterNumber} · was`} Scene{' '}
            {scene.wasSceneNumber}
          </span>
          <span className="truncate text-text">{summary === '' ? 'Empty scene' : summary}</span>
          {scene.useCount > 0 && (
            <span className="text-2xs text-warn">
              {plural(scene.useCount, 'source use')} archived with it
            </span>
          )}
        </div>
        {canWrite && (
          <button
            ref={restoreButton}
            type="button"
            data-testid={planTestIds.restore(scene.id)}
            onClick={() => (chapter.archived ? setAsking(true) : void restore(scene.id))}
            className={buttonClass}
          >
            Restore
          </button>
        )}
      </div>
      {asking && (
        <div
          role="alertdialog"
          aria-label="Restore the chapter too?"
          data-testid={planTestIds.restorePrompt}
          onKeyDown={(event) => event.key === 'Escape' && close()}
          className="flex flex-col gap-1 rounded-sm bg-surface-2 p-2"
        >
          <span>Its chapter {chapter.title} is archived. Restore the chapter too?</span>
          <div className="flex gap-1">
            <button
              type="button"
              autoFocus
              data-testid={planTestIds.restoreBoth}
              onClick={() => void restoreBoth()}
              className={buttonClass}
            >
              Restore both
            </button>
            <button
              type="button"
              data-testid={planTestIds.restoreOnly}
              onClick={close}
              className={buttonClass}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
