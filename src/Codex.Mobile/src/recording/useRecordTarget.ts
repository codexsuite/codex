import type { RecordTarget } from '~/links';
import { useKb, useOutline } from '~/book/queries';

/** What the sheet is recording for, named from the cached outline or Knowledge base: `Scene 1.2 · An Empty Seat`. */
export function useRecordTarget(bookId: string, target: RecordTarget): { label: string | null } {
  const outline = useOutline(bookId);
  const kb = useKb(bookId);
  if (target.kind === 'scene') {
    const scene = outline.data?.acts
      .flatMap((act) => act.chapters)
      .flatMap((chapter) => chapter.scenes)
      .find((candidate) => candidate.id === target.id);
    return { label: scene ? `Scene ${scene.number} · ${scene.title ?? 'Untitled'}` : null };
  }
  const entry = kb.data?.entries.find((candidate) => candidate.id === target.id);
  return { label: entry ? entry.name : null };
}
