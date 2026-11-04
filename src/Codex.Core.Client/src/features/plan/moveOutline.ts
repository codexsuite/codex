import type { Outline, OutlineChapter, OutlineScene } from '../../api/structure';
import { liveChapters } from './format';

/** One reorder, as the server takes it. `afterId` null puts the item first in its target. */
export type Move =
  | { kind: 'act'; id: string; afterId: string | null }
  | { kind: 'chapter'; id: string; actId: string; afterId: string | null }
  | { kind: 'scene'; id: string; chapterId: string; afterId: string | null };

export type MoveKind = Move['kind'];

function insertAfter<T extends { id: string }>(list: T[], item: T, afterId: string | null): T[] {
  const without = list.filter((x) => x.id !== item.id);
  const at = afterId === null ? 0 : without.findIndex((x) => x.id === afterId) + 1;
  return [...without.slice(0, at), item, ...without.slice(at)];
}

// Chapter numbers run on across acts (unnumbered and archived chapters have none); scenes number within their chapter.
function renumber(outline: Outline): Outline {
  let next = 0;
  return {
    ...outline,
    acts: outline.acts.map((act) => ({
      ...act,
      chapters: act.chapters.map((chapter) => {
        const number = chapter.archived || chapter.number === null ? null : ++next;
        return {
          ...chapter,
          number,
          scenes: chapter.scenes.map((scene, i) => ({
            ...scene,
            number: number === null ? `${i + 1}` : `${number}.${i + 1}`,
          })),
        };
      }),
    })),
  };
}

/** The outline as it will read once the server has applied `move` (the server's answer replaces it). */
export function applyMove(outline: Outline, move: Move): Outline {
  if (move.kind === 'act') {
    const act = outline.acts.find((a) => a.id === move.id);
    if (!act) return outline;
    return renumber({ ...outline, acts: insertAfter(outline.acts, act, move.afterId) });
  }
  if (move.kind === 'chapter') {
    const chapter = outline.acts.flatMap((a) => a.chapters).find((c) => c.id === move.id);
    if (!chapter) return outline;
    return renumber({
      ...outline,
      acts: outline.acts.map((act) => {
        const rest = act.chapters.filter((c) => c.id !== move.id);
        return act.id === move.actId
          ? { ...act, chapters: insertAfter(rest, chapter, move.afterId) }
          : { ...act, chapters: rest };
      }),
    });
  }
  const scene = outline.acts
    .flatMap((a) => a.chapters)
    .flatMap((c) => c.scenes)
    .find((s) => s.id === move.id);
  if (!scene) return outline;
  return renumber({
    ...outline,
    acts: outline.acts.map((act) => ({
      ...act,
      chapters: act.chapters.map((chapter) => {
        const rest = chapter.scenes.filter((s) => s.id !== move.id);
        return chapter.id === move.chapterId
          ? { ...chapter, scenes: insertAfter(rest, scene, move.afterId) }
          : { ...chapter, scenes: rest };
      }),
    })),
  });
}

export type Arrow = 'ArrowUp' | 'ArrowDown' | 'ArrowLeft' | 'ArrowRight';

// Moving one step inside `list` (without the item itself): the item ends up at index `index + step`.
function stepWithin<T extends { id: string }>(
  list: T[],
  id: string,
  step: number,
): string | null | undefined {
  const from = list.findIndex((x) => x.id === id);
  const to = from + step;
  if (from < 0 || to < 0 || to >= list.length) return undefined;
  const rest = list.filter((x) => x.id !== id);
  return to === 0 ? null : rest[to - 1].id;
}

/** The move an Alt+arrow asks for, or null at the end of the line: acts and scenes use ↑/↓, chapters ←/→, a scene's
 *  ←/→ takes it to the end of the neighbouring chapter, and ↑/↓ past a chapter's edge (a scene) or an act's edge (a
 *  chapter) carries it into the neighbour. */
export function stepMove(outline: Outline, kind: MoveKind, id: string, key: Arrow): Move | null {
  const step = key === 'ArrowUp' || key === 'ArrowLeft' ? -1 : 1;
  const vertical = key === 'ArrowUp' || key === 'ArrowDown';
  if (kind === 'act') {
    const afterId = vertical ? stepWithin(outline.acts, id, step) : undefined;
    return afterId === undefined ? null : { kind, id, afterId };
  }
  if (kind === 'chapter') {
    const act = outline.acts.find((a) => a.chapters.some((c) => c.id === id));
    if (act === undefined) return null;
    if (vertical) {
      // Alt+↑ / Alt+↓ carry the chapter across the act edge: to the end of the previous act, or the start of the next.
      const neighbour = outline.acts[outline.acts.indexOf(act) + step];
      if (!neighbour) return null;
      const last = liveChapters(neighbour).at(-1);
      return { kind, id, actId: neighbour.id, afterId: step < 0 ? (last?.id ?? null) : null };
    }
    const afterId = stepWithin(liveChapters(act), id, step);
    return afterId === undefined ? null : { kind, id, actId: act.id, afterId };
  }
  const chapters: OutlineChapter[] = outline.acts.flatMap(liveChapters);
  const here = chapters.find((c) => c.scenes.some((s) => s.id === id));
  if (!here) return null;
  if (vertical) {
    const afterId = stepWithin(here.scenes, id, step);
    if (afterId !== undefined) return { kind, id, chapterId: here.id, afterId };
    // Past the first or last scene, the scene crosses into the neighbouring chapter: its end, or its start.
    const neighbour = chapters[chapters.indexOf(here) + step];
    if (!neighbour) return null;
    return {
      kind,
      id,
      chapterId: neighbour.id,
      afterId: step < 0 ? (neighbour.scenes.at(-1)?.id ?? null) : null,
    };
  }
  const target = chapters[chapters.indexOf(here) + step];
  if (!target) return null;
  const last: OutlineScene | undefined = target.scenes[target.scenes.length - 1];
  return { kind, id, chapterId: target.id, afterId: last ? last.id : null };
}
