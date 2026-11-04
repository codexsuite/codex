import { useEffect, useRef, useState, type RefObject } from 'react';
import { useCanWrite } from '../../shell/bookAccess';
import { FindReplacePanel, type FindFlags } from './FindReplacePanel';
import type { Scope } from '@codex/core/features/write/scope';
import { SearchIcon } from './icons';
import { writeTestIds } from './testIds';

interface FindInManuscriptProps {
  /** The scrolling manuscript; its prose editors and AI drafts are searched. */
  scroller: RefObject<HTMLElement | null>;
  /** What the screen shows: Replace searches the same scope. */
  scope: Scope;
  /** Scene heading by scene id, for the matches Replace lists. */
  headings: ReadonlyMap<string, string>;
}

const SEARCHED = '.write-scene-main';
export const findHighlight = 'write-find';
export const findCurrentHighlight = 'write-find-current';

// Text the writer wrote: editable prose, or an AI draft's. Not the chrome of the cards drawn inside the prose
// (labels, buttons, the beat's instruction field), which sit under `contenteditable="false"`.
const isProse = (node: Node) => {
  // A mention is an atom (not itself editable) inside the editable text: its label is prose.
  const inline = node.parentElement?.closest('[data-mention]')?.parentElement ?? node.parentElement;
  const editable = inline?.closest('[contenteditable]');
  return (
    node.parentElement?.closest('.write-draft-body') != null ||
    editable?.getAttribute('contenteditable') === 'true' ||
    // Read-only below write (#80): the scene's own editor is not editable, but its text is still prose.
    (editable?.classList.contains('ProseMirror') === true &&
      editable.parentElement?.closest('[contenteditable]') == null)
  );
};

/** Every match of `query` in the searched text, in reading order (case-insensitive unless `matchCase`). */
function findRanges(root: HTMLElement, query: string, flags: FindFlags): Range[] {
  const ranges: Range[] = [];
  const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const body = flags.wholeWord ? `(?<![\\p{L}\\p{N}_])${escaped}(?![\\p{L}\\p{N}_])` : escaped;
  const pattern = new RegExp(body, flags.matchCase ? 'gu' : 'giu');
  for (const area of root.querySelectorAll(SEARCHED)) {
    const walker = document.createTreeWalker(area, NodeFilter.SHOW_TEXT, {
      acceptNode: (node) => (isProse(node) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT),
    });
    while (walker.nextNode()) {
      const node = walker.currentNode as Text;
      for (const found of node.data.matchAll(pattern)) {
        if (found[0].length === 0) continue;
        const range = document.createRange();
        range.setStart(node, found.index);
        range.setEnd(node, found.index + found[0].length);
        ranges.push(range);
      }
    }
  }
  return ranges;
}

// The editors own their DOM, so matches are drawn with the CSS Custom Highlight API rather than <mark>s.
function draw(ranges: readonly Range[], current: number) {
  if (!('highlights' in CSS)) return;
  if (ranges.length === 0) CSS.highlights.delete(findHighlight);
  else CSS.highlights.set(findHighlight, new Highlight(...ranges));
  const at = ranges[current];
  if (at) CSS.highlights.set(findCurrentHighlight, new Highlight(at));
  else CSS.highlights.delete(findCurrentHighlight);
}

// The toolbar's `Find in manuscript…`: marks every match, shows the count, and Enter / Shift+Enter step through
// them (`2 of 5`), scrolling each into the middle of the manuscript. Esc clears it.
export function FindInManuscript({ scroller, scope, headings }: FindInManuscriptProps) {
  const [query, setQuery] = useState('');
  const [replaceOpen, setReplaceOpen] = useState(false);
  // Find yes, Replace no below write (#80).
  const canWrite = useCanWrite();
  const [flags, setFlags] = useState<FindFlags>({ matchCase: false, wholeWord: false });
  const [count, setCount] = useState(0);
  const [current, setCurrent] = useState(-1);
  const ranges = useRef<Range[]>([]);
  const at = useRef(-1);

  useEffect(() => {
    const root = scroller.current;
    const wanted = query.trim();
    at.current = -1;
    if (!root || wanted === '') {
      ranges.current = [];
      draw([], -1);
      return;
    }
    let frame = 0;
    const search = () => {
      frame = 0;
      ranges.current = findRanges(root, wanted, flags);
      if (at.current >= ranges.current.length) at.current = ranges.current.length - 1;
      setCount(ranges.current.length);
      setCurrent(at.current);
      draw(ranges.current, at.current);
    };
    frame = requestAnimationFrame(search);
    const observer = new MutationObserver(() => {
      if (frame === 0) frame = requestAnimationFrame(search);
    });
    observer.observe(root, { subtree: true, childList: true, characterData: true });
    return () => {
      observer.disconnect();
      cancelAnimationFrame(frame);
    };
  }, [scroller, query, flags]);

  useEffect(() => () => draw([], -1), []);

  const step = (by: 1 | -1) => {
    const root = scroller.current;
    const total = ranges.current.length;
    if (!root || total === 0) return;
    at.current = at.current < 0 && by < 0 ? total - 1 : (at.current + by + total) % total;
    setCurrent(at.current);
    const range = ranges.current[at.current]!;
    draw(ranges.current, at.current);
    const box = range.getBoundingClientRect();
    const frame = root.getBoundingClientRect();
    root.scrollTo({
      top: root.scrollTop + box.top - frame.top - root.clientHeight / 2,
      behavior: 'smooth',
    });
  };

  return (
    <span className="relative flex items-center gap-1">
      <label className="write-search">
        <SearchIcon />
        <input
          type="search"
          data-testid={writeTestIds.find}
          aria-label="Find in manuscript"
          placeholder="Find in manuscript…"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setCurrent(-1);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              step(event.shiftKey ? -1 : 1);
            }
            if (event.key === 'Escape') {
              setQuery('');
              setCurrent(-1);
            }
          }}
        />
        {query.trim() !== '' && (
          <span data-testid={writeTestIds.findCount} className="write-search-count">
            {current >= 0 ? `${current + 1} of ${count}` : count}
          </span>
        )}
      </label>
      {canWrite && (
        <button
          type="button"
          data-testid={writeTestIds.replaceToggle}
          aria-expanded={replaceOpen}
          className="write-chip"
          onClick={() => setReplaceOpen((open) => !open)}
        >
          Replace
        </button>
      )}
      {canWrite && replaceOpen && (
        <FindReplacePanel
          scroller={scroller}
          scope={scope}
          headings={headings}
          query={query.trim()}
          flags={flags}
          onFlags={setFlags}
          onClose={() => setReplaceOpen(false)}
        />
      )}
    </span>
  );
}
