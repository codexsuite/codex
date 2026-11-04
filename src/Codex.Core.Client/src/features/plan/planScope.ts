import type { Outline, OutlineAct, OutlineChapter } from '../../api/structure';
import { actName, actScenes, liveChapters } from './format';

// The book bar's Scope on Plan: the whole manuscript, or one act or chapter. The board and the outline table show only
// what is in scope, and the counts beside the picker add up just that.
export type PlanScope = { kind: 'book' } | { kind: 'act' | 'chapter'; id: string };

export const wholeBook: PlanScope = { kind: 'book' };

export const scopeValue = (scope: PlanScope) =>
  scope.kind === 'book' ? 'book' : `${scope.kind}:${scope.id}`;

export function parseScope(value: string): PlanScope {
  const [kind, id] = value.split(':');
  return (kind === 'act' || kind === 'chapter') && id ? { kind, id } : wholeBook;
}

/** The scope as the outline has it now: an act or chapter that is gone (deleted, archived) falls back to the book. */
export function resolveScope(outline: Outline | undefined, scope: PlanScope): PlanScope {
  if (scope.kind === 'book' || outline === undefined) return scope;
  const found =
    scope.kind === 'act'
      ? outline.acts.some((a) => a.id === scope.id)
      : outline.acts.some((a) => liveChapters(a).some((c) => c.id === scope.id));
  return found ? scope : wholeBook;
}

export const actInScope = (act: OutlineAct, scope: PlanScope) =>
  scope.kind === 'book' ||
  (scope.kind === 'act' ? act.id === scope.id : act.chapters.some((c) => c.id === scope.id));

export const chapterInScope = (chapter: OutlineChapter, scope: PlanScope) =>
  scope.kind !== 'chapter' || chapter.id === scope.id;

/** The words of the live scenes in scope. */
export function scopeWords(outline: Outline, scope: PlanScope): number {
  return outline.acts
    .filter((act) => actInScope(act, scope))
    .flatMap((act) =>
      scope.kind === 'chapter'
        ? liveChapters(act)
            .filter((c) => chapterInScope(c, scope))
            .flatMap((c) => c.scenes)
        : actScenes(act),
    )
    .reduce((sum, scene) => sum + scene.words, 0);
}

/** The picker's options: Full manuscript, each act, then each live chapter. */
export function scopeOptions(outline: Outline | undefined): { value: string; label: string }[] {
  const acts = outline?.acts ?? [];
  return [
    { value: 'book', label: 'Full manuscript' },
    ...acts.map((act, index) => ({ value: `act:${act.id}`, label: actName(index, act.title) })),
    ...acts.flatMap(liveChapters).map((chapter) => ({
      value: `chapter:${chapter.id}`,
      label:
        chapter.number === null ? chapter.title : `Chapter ${chapter.number}: ${chapter.title}`,
    })),
  ];
}
