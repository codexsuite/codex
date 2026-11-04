import { useEffect } from 'react';
import type { RefObject } from 'react';
import type { ScrollTarget } from '@codex/core/features/write/manuscriptView';

const SETTLE_MS = 1500;
const FLASH_MS = 1600;
const MARGIN_PX = 16;

const selectorFor = (target: ScrollTarget) =>
  target.kind === 'scene' ? `[data-scene-id="${target.id}"]` : `[data-chapter-id="${target.id}"]`;

export function scrollContainerTo(container: HTMLElement, element: HTMLElement) {
  const offset = element.getBoundingClientRect().top - container.getBoundingClientRect().top;
  container.scrollTop += offset - MARGIN_PX;
}

function flash(element: HTMLElement): number {
  delete element.dataset.flash;
  element.getBoundingClientRect(); // restart the animation
  element.dataset.flash = 'true';
  return window.setTimeout(() => delete element.dataset.flash, FLASH_MS);
}

/**
 * Scrolls the manuscript to a scene or chapter and flashes it. The prose editors mount after the first paint and
 * change heights, so the scroll is repeated while the content resizes (for 1.5 s, or until the reader scrolls).
 */
export function useScrollToTarget(
  container: RefObject<HTMLElement | null>,
  target: ScrollTarget | null,
) {
  const kind = target?.kind;
  const id = target?.id;
  useEffect(() => {
    const root = container.current;
    if (!root || !kind || !id) return;
    const element = root.querySelector<HTMLElement>(selectorFor({ kind, id }));
    if (!element) return;

    scrollContainerTo(root, element);
    const flashTimer = flash(element);
    const startedAt = performance.now();
    const content = root.firstElementChild;
    const observer = new ResizeObserver(() => {
      if (performance.now() - startedAt > SETTLE_MS) observer.disconnect();
      else scrollContainerTo(root, element);
    });
    const stop = () => observer.disconnect();
    if (content) observer.observe(content);
    root.addEventListener('wheel', stop, { once: true });
    root.addEventListener('pointerdown', stop, { once: true });
    root.addEventListener('keydown', stop, { once: true });
    return () => {
      stop();
      root.removeEventListener('wheel', stop);
      root.removeEventListener('pointerdown', stop);
      root.removeEventListener('keydown', stop);
      window.clearTimeout(flashTimer);
      delete element.dataset.flash;
    };
  }, [container, kind, id]);
}
