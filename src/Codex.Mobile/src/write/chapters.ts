import type { Outline, OutlineScene } from '@codex/core/api/structure';

// The chapters of the outline as Write reads them: live chapters in reading order, grouped by act for the jump sheet.

export interface WriteChapter {
  id: string;
  actId: string;
  actTitle: string;
  number: number | null;
  title: string;
  /** `3 · The Bell at Matins`, or just the title for an unnumbered chapter. */
  label: string;
  scenes: OutlineScene[];
  words: number;
}

export interface ActChapters {
  id: string;
  title: string;
  chapters: WriteChapter[];
}

export function chapterLabel(number: number | null, title: string): string {
  return number === null ? title : `${number} · ${title}`;
}

/** Acts that hold at least one live chapter, in order. */
export function actsOf(outline: Outline): ActChapters[] {
  return outline.acts
    .map((act) => ({
      id: act.id,
      title: act.title,
      chapters: act.chapters
        .filter((chapter) => !chapter.archived)
        .map((chapter): WriteChapter => ({
          id: chapter.id,
          actId: act.id,
          actTitle: act.title,
          number: chapter.number,
          title: chapter.title,
          label: chapterLabel(chapter.number, chapter.title),
          scenes: chapter.scenes,
          words: chapter.scenes.reduce((sum, scene) => sum + scene.words, 0),
        })),
    }))
    .filter((act) => act.chapters.length > 0);
}

/** Every live chapter of the book in reading order. */
export const chaptersOf = (outline: Outline): WriteChapter[] =>
  actsOf(outline).flatMap((act) => act.chapters);

export interface WriteTarget {
  chapter: WriteChapter;
  /** The scene to scroll to: the linked scene, null when the link named a chapter or nothing. */
  scrollToSceneId: string | null;
}

/**
 * Where Write is looking: a linked scene wins (its chapter, scrolled to it), then a linked chapter, then the first
 * chapter. A link to something that no longer exists (archived, deleted) falls through to the next rule.
 */
export function resolveTarget(
  chapters: readonly WriteChapter[],
  at: { sceneId?: string; chapterId?: string },
): WriteTarget | null {
  if (at.sceneId) {
    const chapter = chapters.find((c) => c.scenes.some((s) => s.id === at.sceneId));
    if (chapter) return { chapter, scrollToSceneId: at.sceneId };
  }
  const named = at.chapterId ? chapters.find((c) => c.id === at.chapterId) : undefined;
  const chapter = named ?? chapters[0];
  return chapter ? { chapter, scrollToSceneId: null } : null;
}

export function neighbours(
  chapters: readonly WriteChapter[],
  chapterId: string,
): { previous: WriteChapter | null; next: WriteChapter | null } {
  const at = chapters.findIndex((c) => c.id === chapterId);
  return { previous: chapters[at - 1] ?? null, next: chapters[at + 1] ?? null };
}

/** `Scene 2` for the scene numbered `1.2`; a scene number without an act part is used as it is. */
export function sceneName(number: string): string {
  return `Scene ${number.includes('.') ? number.slice(number.lastIndexOf('.') + 1) : number}`;
}
