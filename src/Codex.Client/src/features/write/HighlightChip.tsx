import { useEffect, useRef, useState, type RefObject } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { withHashKey } from '../kb/hashParams';
import { useKbInfo } from './kbInfo';
import { scrollContainerTo } from './scrollToTarget';
import { useWriteHighlight } from './useWriteHighlight';
import { writeTestIds } from './testIds';

interface HighlightChipProps {
  bookId: string;
  /** The scrolling manuscript; its `.kb-hl` elements are the highlighted entry's uses. */
  scroller: RefObject<HTMLElement | null>;
}

const USES = '.kb-hl';
export const currentUseHighlight = 'kb-current';

// The use being looked at is drawn with the CSS Custom Highlight API: the editors own their DOM and would undo an
// attribute set on it. Without the API the page still scrolls to the use.
function markCurrent(element: HTMLElement | null) {
  if (!('highlights' in CSS)) return;
  if (!element) {
    CSS.highlights.delete(currentUseHighlight);
    return;
  }
  const range = document.createRange();
  range.selectNodeContents(element);
  CSS.highlights.set(currentUseHighlight, new Highlight(range));
}

// The toolbar chip for the highlighted Knowledge base entry: `Name · 1 of n`, arrows that scroll to each use in
// reading order (a mention, a name or alias in the text, or a Codex addition), and × to clear the highlight (the
// stored one and `#kb=`). The uses are the marks the editors draw, so the count follows the text as it changes.
export function HighlightChip({ bookId, scroller }: HighlightChipProps) {
  const [highlighted, setHighlighted] = useWriteHighlight(bookId);
  const infos = useKbInfo(bookId);
  const navigate = useNavigate();
  const { pathname, search, hash } = useLocation();
  const [count, setCount] = useState(0);
  const [current, setCurrent] = useState(0);
  // -1 until the first step: the chip reads "1 of n" and the first › stops on use 1 (‹ on the last).
  const at = useRef(-1);

  // Recount whenever the marks change. The page keys the chip by entry, so a new entry starts at its first use.
  useEffect(() => {
    const root = scroller.current;
    if (!root || !highlighted) return;
    let frame = 0;
    const recount = () => {
      frame = 0;
      setCount(root.querySelectorAll(USES).length);
    };
    const schedule = () => {
      if (frame === 0) frame = requestAnimationFrame(recount);
    };
    recount();
    const observer = new MutationObserver(schedule);
    observer.observe(root, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['class'],
    });
    return () => {
      observer.disconnect();
      cancelAnimationFrame(frame);
      markCurrent(null);
    };
  }, [scroller, highlighted]);

  const info = highlighted ? infos.get(highlighted) : undefined;
  if (!highlighted || !info) return null;

  const step = (direction: 1 | -1) => {
    const root = scroller.current;
    const uses = root ? [...root.querySelectorAll<HTMLElement>(USES)] : [];
    if (!root || uses.length === 0) return;
    const next =
      at.current < 0
        ? direction === 1
          ? 0
          : uses.length - 1
        : (at.current + direction + uses.length) % uses.length;
    at.current = next;
    setCurrent(next);
    const target = uses[next]!;
    markCurrent(target);
    scrollContainerTo(root, target);
  };
  const clear = () => {
    markCurrent(null);
    setHighlighted(null);
    void navigate({ pathname, search, hash: withHashKey(hash, 'kb', null) }, { replace: true });
  };
  const shown = Math.min(current, Math.max(count - 1, 0)) + 1;

  return (
    <span
      data-testid={writeTestIds.highlightChip}
      data-entry-id={highlighted}
      className="write-highlight-chip"
      style={{ ['--cat' as string]: info.colour }}
    >
      <span className="write-addition-dot" />
      <span data-testid={writeTestIds.highlightName} className="font-medium">
        {info.name}
      </span>
      <span data-testid={writeTestIds.highlightPosition} className="text-muted tabular-nums">
        {count === 0 ? 'no uses in the text' : `${shown} of ${count}`}
      </span>
      <button
        type="button"
        data-testid={writeTestIds.highlightPrevious}
        aria-label="Previous use"
        disabled={count === 0}
        onClick={() => step(-1)}
      >
        ‹
      </button>
      <button
        type="button"
        data-testid={writeTestIds.highlightNext}
        aria-label="Next use"
        disabled={count === 0}
        onClick={() => step(1)}
      >
        ›
      </button>
      <button
        type="button"
        data-testid={writeTestIds.highlightClear}
        aria-label="Clear highlight"
        onClick={clear}
      >
        ×
      </button>
    </span>
  );
}
