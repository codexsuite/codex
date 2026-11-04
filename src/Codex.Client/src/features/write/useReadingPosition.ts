import { useEffect, useState, type RefObject } from 'react';

/**
 * The scene being read: the first one whose bottom is below the top 60px of the manuscript, as in
 * design/screens/write.html. Follows scrolling; `content` changes (a new scope) look again.
 */
export function useReadingPosition(
  scroller: RefObject<HTMLElement | null>,
  content: unknown,
): string | null {
  const [sceneId, setSceneId] = useState<string | null>(null);
  useEffect(() => {
    const root = scroller.current;
    if (!root) return;
    let frame = 0;
    const look = () => {
      frame = 0;
      const top = root.getBoundingClientRect().top + 60;
      const scenes = [...root.querySelectorAll<HTMLElement>('[data-scene-id]')];
      const found = scenes.find((s) => s.getBoundingClientRect().bottom > top) ?? scenes.at(-1);
      setSceneId(found?.dataset.sceneId ?? null);
    };
    const onScroll = () => {
      if (frame === 0) frame = requestAnimationFrame(look);
    };
    look();
    root.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      root.removeEventListener('scroll', onScroll);
      cancelAnimationFrame(frame);
    };
  }, [scroller, content]);
  return sceneId;
}
