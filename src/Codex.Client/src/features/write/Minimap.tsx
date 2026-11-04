import { useEffect, useRef, useState, type PointerEvent, type RefObject } from 'react';
import type { ActView } from '@codex/core/features/write/manuscriptView';
import { writeTestIds } from './testIds';

interface MinimapProps {
  acts: readonly ActView[];
  /** The scrolling manuscript element; its first child is the content. */
  scroller: RefObject<HTMLElement | null>;
  /** Scenes that use the highlighted Knowledge base entry; their ticks are accent. */
  usedScenes: ReadonlySet<string>;
}

interface Box {
  top: number;
  height: number;
}

const NOT_MEASURED: Box = { top: 0, height: 0 };

// A mark per chapter and a tick per scene, placed and sized as the scene sits in the document. Empty scenes are
// hollow; scenes with a pending AI draft, and scenes using the highlighted entry, are accent. Click or drag to jump.
export function Minimap({ acts, scroller, usedScenes }: MinimapProps) {
  const [boxes, setBoxes] = useState<ReadonlyMap<string, Box>>(new Map());
  const view = useRef<HTMLSpanElement>(null);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = scroller.current;
    const content = container?.firstElementChild;
    const map = root.current;
    if (!container || !content || !map) return;

    const measure = () => {
      const total = container.scrollHeight || 1;
      const height = map.clientHeight;
      const origin = content.getBoundingClientRect().top;
      const next = new Map<string, Box>();
      for (const element of content.querySelectorAll<HTMLElement>(
        '[data-scene-id], [data-chapter-id]',
      )) {
        const rect = element.getBoundingClientRect();
        next.set(element.dataset.sceneId ?? element.dataset.chapterId ?? '', {
          top: ((rect.top - origin) / total) * height,
          height: (rect.height / total) * height,
        });
      }
      setBoxes(next);
    };
    const position = () => {
      const box = view.current;
      if (!box) return;
      const total = container.scrollHeight || 1;
      box.style.top = `${(container.scrollTop / total) * map.clientHeight}px`;
      box.style.height = `${Math.max(12, (container.clientHeight / total) * map.clientHeight)}px`;
    };
    const observer = new ResizeObserver(() => {
      measure();
      position();
    });
    observer.observe(content);
    observer.observe(map);
    container.addEventListener('scroll', position);
    return () => {
      observer.disconnect();
      container.removeEventListener('scroll', position);
    };
  }, [scroller, acts]);

  const jump = (event: PointerEvent<HTMLDivElement>) => {
    const container = scroller.current;
    if (!container) return;
    const rect = event.currentTarget.getBoundingClientRect();
    container.scrollTo({
      top:
        ((event.clientY - rect.top) / rect.height) * container.scrollHeight -
        container.clientHeight / 2,
    });
  };

  return (
    <div
      ref={root}
      role="presentation"
      data-testid={writeTestIds.minimap}
      className="write-minimap relative min-h-0 cursor-pointer touch-none border-l border-border bg-surface max-[760px]:hidden"
      style={{ width: 'var(--minimap-width)' }}
      onPointerDown={(event) => {
        event.currentTarget.setPointerCapture(event.pointerId);
        jump(event);
      }}
      onPointerMove={(event) => {
        if (event.buttons === 1) jump(event);
      }}
    >
      {acts.flatMap((act) =>
        act.chapters.flatMap((chapter) => {
          const mark = boxes.get(chapter.chapter.id) ?? NOT_MEASURED;
          return [
            <span
              key={chapter.chapter.id}
              data-testid={writeTestIds.minimapChapter(chapter.chapter.id)}
              className="absolute right-[3px] left-[3px] h-0.5 rounded-[1px] bg-text opacity-55"
              style={{ top: mark.top }}
            />,
            ...chapter.scenes.map(({ scene }) => {
              const tick = boxes.get(scene.id) ?? NOT_MEASURED;
              const kind = scene.hasPendingDraft
                ? 'pending'
                : scene.words === 0
                  ? 'empty'
                  : 'written';
              return (
                <span
                  key={scene.id}
                  data-testid={writeTestIds.minimapTick(scene.id)}
                  data-kind={kind}
                  data-used={usedScenes.has(scene.id)}
                  className="write-minimap-tick absolute right-[7px] left-[7px] rounded-[1px]"
                  style={{ top: tick.top + 3, height: Math.max(3, tick.height - 4) }}
                />
              );
            }),
          ];
        }),
      )}
      <span
        ref={view}
        className="write-minimap-view pointer-events-none absolute right-px left-px rounded-[3px]"
      />
    </div>
  );
}
