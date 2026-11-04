import { useEffect, useRef, type MouseEvent } from 'react';
import { layoutTestIds } from './testIds';
import { PAGES_CLASS } from './scopeCss';
import type { PagedInput } from './usePagedPreview';
import { usePagedPreview } from './usePagedPreview';
import type { PagedState } from './usePagedPreview';

export type CanvasView = 'single' | 'spread';

export interface CanvasFocus {
  sceneId: string;
  /** Scroll to the scene as well as flash it. */
  scroll: boolean;
  nonce: number;
}

interface PageCanvasProps {
  input: PagedInput | null;
  view: CanvasView;
  zoom: number;
  showIssues: boolean;
  focus: CanvasFocus | null;
  onPaged: (state: PagedState) => void;
  onBlock: (block: HTMLElement) => void;
}

// The pages on `--color-bg`. Paged.js builds the pages inside the host element; React only owns the host, its class
// and the click that opens a block's card.
export function PageCanvas({
  input,
  view,
  zoom,
  showIssues,
  focus,
  onPaged,
  onBlock,
}: PageCanvasProps) {
  const host = useRef<HTMLDivElement>(null);
  const paged = usePagedPreview(host, input);

  useEffect(() => {
    onPaged(paged);
  }, [paged, onPaged]);

  useEffect(() => {
    const element = host.current;
    if (focus === null || element === null || paged.status !== 'ready') return;
    const blocks = Array.from(
      element.querySelectorAll<HTMLElement>(`[data-scene="${focus.sceneId}"]`),
    );
    if (focus.scroll) blocks[0]?.scrollIntoView({ block: 'center' });
    for (const block of blocks) {
      block.classList.remove('codex-flash');
      void block.offsetWidth;
      block.classList.add('codex-flash');
      block.addEventListener('animationend', () => block.classList.remove('codex-flash'), {
        once: true,
      });
    }
  }, [focus, paged.status]);

  const click = (event: MouseEvent<HTMLDivElement>) => {
    const block = (event.target as HTMLElement).closest<HTMLElement>('[data-scene]');
    if (block !== null) onBlock(block);
  };

  return (
    <div className="min-h-0 flex-1 overflow-auto bg-bg p-4" data-testid={layoutTestIds.canvas}>
      <div
        ref={host}
        role="presentation"
        onClick={click}
        style={{ zoom }}
        className={`${PAGES_CLASS} layout-${view}${showIssues ? '' : ' layout-hide-issues'}`}
      />
    </div>
  );
}
