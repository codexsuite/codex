import type { KbUse } from '@codex/core/api/kbEntries';
import type { KbAddition } from '@codex/core/api/write';

// Pure helpers of the entry screen: how a use is labelled and which additions belong to the entry.

/** `Chapter 2 · Scene 1` from a use (`number` is `2.1`; a chapter without a number says `Chapter`). */
export function useLabel(use: Pick<KbUse, 'chapterNumber' | 'chapterTitle' | 'number'>): string {
  const scene = use.number.includes('.')
    ? use.number.slice(use.number.lastIndexOf('.') + 1)
    : use.number;
  const chapter =
    use.chapterNumber !== null
      ? `Chapter ${use.chapterNumber}`
      : use.chapterTitle.trim() !== ''
        ? use.chapterTitle.trim()
        : 'Chapter';
  return `${chapter} · Scene ${scene}`;
}

/** The uses in this book: a series entry also lists scenes of the series' other books, which this app cannot open. */
export function usesInBook(uses: KbUse[], bookId: string): KbUse[] {
  return uses.filter((u) => u.bookId === bookId);
}

export interface EntryAddition {
  id: string;
  body: string;
  /** `Chapter 2 · Scene 1`, or null when the scene is not among the entry's uses. */
  where: string | null;
}

/** The book's additions for one entry, each labelled by the use that names its scene. */
export function additionsFor(
  additions: KbAddition[],
  entryId: string,
  uses: KbUse[],
): EntryAddition[] {
  return additions
    .filter((a) => a.entryId === entryId)
    .map((a) => {
      const use = uses.find((u) => u.sceneId === a.sceneId);
      return { id: a.id, body: a.body, where: use ? useLabel(use) : null };
    });
}

/** Aliases the author typed, trimmed; empty and repeated ones (any case) are dropped. */
export function acceptableAlias(typed: string, existing: string[], name: string): string | null {
  const alias = typed.trim();
  if (alias === '') return null;
  const lower = alias.toLowerCase();
  if (lower === name.toLowerCase() || existing.some((a) => a.toLowerCase() === lower)) return null;
  return alias;
}
