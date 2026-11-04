import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useOutline } from '../../api/outlineQueries';
import type { OutlineScene } from '@codex/core/api/structure';
import { writeLink } from '@codex/core/shell/links';
import { liveChapters } from '@codex/core/features/plan/format';
import { SceneLabels } from './SceneLabels';
import { planTestIds } from './testIds';
import { useReorder } from './useReorder';
import { useSceneArchive } from './useSceneArchive';
import { useCanWrite } from '../../shell/bookAccess';

const itemClass =
  'block w-full rounded-sm border-0 bg-transparent px-2 py-1 text-left text-sm text-text no-underline hover:bg-accent-soft';

// The card's ⋯ menu: Open in Write, Labels, Move to chapter…, Merge into previous scene, Archive scene.
export function SceneMenu({ scene }: { scene: OutlineScene }) {
  const { bookId = '' } = useParams();
  const [open, setOpen] = useState(false);
  const [labelsOpen, setLabelsOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const { archive, merge } = useSceneArchive();
  const [moveOpen, setMoveOpen] = useState(false);
  const reorder = useReorder(bookId);
  const { data: outline } = useOutline(bookId);
  const chapters = (outline?.acts ?? []).flatMap(liveChapters);
  const here = chapters.find((c) => c.scenes.some((s) => s.id === scene.id));
  const targets = chapters.filter((c) => c.id !== here?.id);
  const hasPrevious = here !== undefined && here.scenes[0]?.id !== scene.id;

  const close = () => {
    setOpen(false);
    setLabelsOpen(false);
    setMoveOpen(false);
  };

  useEffect(() => {
    if (!open) return;
    const dismiss = (event: Event) => {
      if (
        event instanceof KeyboardEvent
          ? event.key === 'Escape'
          : !root.current?.contains(event.target as Node)
      ) {
        setOpen(false);
        setLabelsOpen(false);
        setMoveOpen(false);
      }
    };
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', dismiss);
    return () => {
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', dismiss);
    };
  }, [open]);

  // Below write (#80) the menu only opens the scene in Write.
  const canWrite = useCanWrite();
  return (
    <div ref={root} className="relative ml-auto">
      <button
        type="button"
        data-testid={planTestIds.sceneMore(scene.id)}
        aria-label="Scene actions"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => (open ? close() : setOpen(true))}
        className="inline-grid size-[22px] place-items-center rounded-xs border-0 bg-transparent p-0 text-muted opacity-0 group-hover:opacity-100 focus-visible:opacity-100 hover:bg-surface-2 hover:text-text"
      >
        ⋯
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 z-20 mt-1 min-w-44 rounded-md border border-border bg-surface p-1 shadow-raised"
        >
          <Link
            role="menuitem"
            to={writeLink(bookId, { scene: scene.number })}
            data-testid={planTestIds.sceneOpenInWrite(scene.id)}
            onClick={close}
            className={itemClass}
          >
            Open in Write
          </Link>
          {canWrite && (
            <>
              <button
                type="button"
                role="menuitem"
                aria-expanded={labelsOpen}
                data-testid={planTestIds.sceneLabels(scene.id)}
                onClick={() => setLabelsOpen(!labelsOpen)}
                className={itemClass}
              >
                Labels
              </button>
              {labelsOpen && <SceneLabels scene={scene} />}
              {targets.length > 0 && (
                <button
                  type="button"
                  role="menuitem"
                  aria-expanded={moveOpen}
                  data-testid={planTestIds.sceneMove(scene.id)}
                  onClick={() => setMoveOpen(!moveOpen)}
                  className={itemClass}
                >
                  Move to chapter…
                </button>
              )}
              {moveOpen &&
                targets.map((chapter) => (
                  <button
                    key={chapter.id}
                    type="button"
                    role="menuitem"
                    data-testid={planTestIds.sceneMoveTarget(chapter.id)}
                    onClick={() => {
                      close();
                      void reorder({
                        kind: 'scene',
                        id: scene.id,
                        chapterId: chapter.id,
                        afterId: chapter.scenes.at(-1)?.id ?? null,
                      });
                    }}
                    className={`${itemClass} pl-4`}
                  >
                    {chapter.number === null
                      ? chapter.title
                      : `Chapter ${chapter.number} · ${chapter.title}`}
                  </button>
                ))}
              {hasPrevious && (
                <button
                  type="button"
                  role="menuitem"
                  data-testid={planTestIds.sceneMerge(scene.id)}
                  onClick={() => {
                    close();
                    void merge(scene.id);
                  }}
                  className={itemClass}
                >
                  Merge into previous scene
                </button>
              )}
              <button
                type="button"
                role="menuitem"
                data-testid={planTestIds.sceneArchive(scene.id)}
                onClick={() => {
                  close();
                  void archive(scene.id);
                }}
                className={itemClass}
              >
                Archive scene
              </button>
            </>
          )}
        </div>
      )}
    </div>
  );
}
