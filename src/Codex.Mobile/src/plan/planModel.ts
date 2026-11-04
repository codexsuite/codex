import type { Kb } from '@codex/core/api/kb';
import type { Outline, OutlineAct, OutlineChapter, OutlineScene } from '@codex/core/api/structure';
import { liveChapters, sceneWords } from '@codex/core/features/plan/format';
import type { CategoryName } from '~/theme';
import { actInScope, chapterInScope, type PlanScope } from '@codex/core/features/plan/planScope';

// The Plan tab's list, shaped from the cached outline: one section per act in scope (sticky header), and under an open
// act the chapter headings and scene cards in reading order. Archived chapters are not drawn.

export type PlanRow =
  | { kind: 'chapter'; chapter: OutlineChapter; scenes: number; words: number }
  | { kind: 'scene'; scene: OutlineScene }
  | { kind: 'empty' };

export interface PlanSection {
  act: OutlineAct;
  /** Position among all the book's acts (not just those in scope), so "Act 2" stays "Act 2" under a scope. */
  index: number;
  chapters: number;
  words: number;
  collapsed: boolean;
  data: PlanRow[];
}

export function buildPlanSections(
  outline: Outline,
  scope: PlanScope,
  isCollapsed: (actId: string, index: number) => boolean,
): PlanSection[] {
  return outline.acts.flatMap((act, index) => {
    if (!actInScope(act, scope)) return [];
    const chapters = liveChapters(act).filter((chapter) => chapterInScope(chapter, scope));
    const collapsed = isCollapsed(act.id, index);
    const rows: PlanRow[] =
      chapters.length === 0
        ? [{ kind: 'empty' }]
        : chapters.flatMap((chapter) => [
            {
              kind: 'chapter' as const,
              chapter,
              scenes: chapter.scenes.length,
              words: sceneWords(chapter.scenes),
            },
            ...chapter.scenes.map((scene) => ({ kind: 'scene' as const, scene })),
          ]);
    return [
      {
        act,
        index,
        chapters: chapters.length,
        words: chapters.reduce((sum, chapter) => sum + sceneWords(chapter.scenes), 0),
        collapsed,
        data: collapsed ? [] : rows,
      },
    ];
  });
}

export function rowKey(row: PlanRow): string {
  switch (row.kind) {
    case 'chapter':
      return `chapter-${row.chapter.id}`;
    case 'scene':
      return `scene-${row.scene.id}`;
    case 'empty':
      return 'empty';
  }
}

/** The scene's own number inside its chapter: "3.2" is 2; an unnumbered chapter's scenes are already just the number. */
export function sceneNumberInChapter(number: string): string {
  const at = number.lastIndexOf('.');
  return at === -1 ? number : number.slice(at + 1);
}

/** The sheet's overline: `Chapter 3 · Scene 2`, or `Scene 2` where the chapter has no number. */
export function sceneHeading(number: string): string {
  const at = number.lastIndexOf('.');
  return at === -1
    ? `Scene ${number}`
    : `Chapter ${number.slice(0, at)} · Scene ${number.slice(at + 1)}`;
}

/** Finds a scene anywhere in the outline (archived chapters excluded). */
export function findScene(outline: Outline, sceneId: string): OutlineScene | undefined {
  for (const act of outline.acts) {
    for (const chapter of liveChapters(act)) {
      const found = chapter.scenes.find((scene) => scene.id === sceneId);
      if (found !== undefined) return found;
    }
  }
  return undefined;
}

const categoryNames: readonly CategoryName[] = ['blog', 'law', 'story', 'research', 'person'];

/** Each entry's category tag when it is one the theme has a colour for (a chip's square), by entry id. */
export function entryCategories(kb: Kb): Map<string, CategoryName> {
  const tags = new Map(kb.categories.map((category) => [category.id, category.tag]));
  const result = new Map<string, CategoryName>();
  for (const entry of kb.entries) {
    const tag = categoryNames.find((name) => name === tags.get(entry.categoryId));
    if (tag !== undefined) result.set(entry.id, tag);
  }
  return result;
}

/** Additions per scene, from the book's additions list. */
export function additionCounts(additions: readonly { sceneId: string }[]): Map<string, number> {
  const counts = new Map<string, number>();
  for (const addition of additions) {
    counts.set(addition.sceneId, (counts.get(addition.sceneId) ?? 0) + 1);
  }
  return counts;
}
