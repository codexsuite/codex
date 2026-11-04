import { useEffect, useState, type RefObject } from 'react';
import { beatDrag } from './beatDrag';
import { useBlockOps } from './useBlockOps';

/** Where a drop at `clientY` lands among a scene's top-level blocks, and where to draw the line. */
function placeIn(section: HTMLElement, clientY: number) {
  const editor = section.querySelector('.ProseMirror');
  const blocks = editor
    ? [...editor.children].filter(
        (el) => !el.classList.contains('ProseMirror-gapcursor') && el.tagName !== 'BR',
      )
    : [];
  const index = blocks.filter((el) => {
    const rect = el.getBoundingClientRect();
    return rect.top + rect.height / 2 < clientY;
  }).length;
  const origin = section.getBoundingClientRect().top;
  const next = blocks[index];
  const last = blocks[blocks.length - 1];
  const edge = next
    ? next.getBoundingClientRect().top
    : last
      ? last.getBoundingClientRect().bottom
      : origin;
  return { index, top: edge - origin };
}

// A scene row as a drop target for a dragged beat; returns where the drop line is drawn, or null. The listeners are
// native and in the capture phase, so ProseMirror (which sits inside the row) never treats the drop as its own.
export function useBeatDrop(sceneId: string, row: RefObject<HTMLElement | null>) {
  const ops = useBlockOps();
  const [line, setLine] = useState<number | null>(null);

  useEffect(() => {
    const section = row.current;
    if (!section) return;
    const over = (event: DragEvent) => {
      if (!beatDrag.get()) return;
      event.preventDefault();
      event.stopPropagation();
      setLine(placeIn(section, event.clientY).top);
    };
    const leave = (event: DragEvent) => {
      if (!section.contains(event.relatedTarget as Node | null)) setLine(null);
    };
    const drop = (event: DragEvent) => {
      const drag = beatDrag.get();
      if (!drag) return;
      event.preventDefault();
      event.stopPropagation();
      setLine(null);
      beatDrag.end();
      void ops.moveBeat(drag.beatId, drag.sceneId, sceneId, placeIn(section, event.clientY).index);
    };
    section.addEventListener('dragover', over, true);
    section.addEventListener('dragleave', leave);
    section.addEventListener('drop', drop, true);
    return () => {
      section.removeEventListener('dragover', over, true);
      section.removeEventListener('dragleave', leave);
      section.removeEventListener('drop', drop, true);
    };
  }, [row, sceneId, ops]);

  return line;
}
