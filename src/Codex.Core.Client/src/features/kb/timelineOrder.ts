import type { KbTimelineNote, KbUse } from '../../api/kbEntries';

/**
 * The notes that sit after the entry's first use in the book (cross-cutting rule R3): a scene move can put an addition
 * behind a scene that already mentions the entry. `usedIn` is in book order. Maps note id to the number of that first
 * use; a warning only, never a block.
 */
export function lateNotes(usedIn: KbUse[], notes: KbTimelineNote[]): ReadonlyMap<string, string> {
  const firstUse = usedIn.findIndex((use) => use.kinds.some((k) => k.kind !== 'timeline'));
  const late = new Map<string, string>();
  if (firstUse < 0) return late;
  for (const note of notes) {
    const at = usedIn.findIndex((use) => use.sceneId === note.sceneId);
    if (at > firstUse) late.set(note.id, usedIn[firstUse].number);
  }
  return late;
}
