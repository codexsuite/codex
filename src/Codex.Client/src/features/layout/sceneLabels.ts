import type { Outline } from '@codex/core/api/structure';

export interface SceneInfo {
  id: string;
  /** Display number, `5.2`, as the deep links and Write use it. */
  number: string;
  /** `Ch 5 · Sc 2`, as the preview's blocks are labelled. */
  label: string;
}

export interface ScopeOption {
  /** `book`, `chapter:<id>` or `scene:<id>`. */
  value: string;
  label: string;
  indent: boolean;
}

// What the Layout toolbar can preview, from the outline: the whole book, each live chapter, and its scenes.
export function scopeOptions(outline: Outline): ScopeOption[] {
  const options: ScopeOption[] = [{ value: 'book', label: 'Whole book', indent: false }];
  for (const chapter of outline.acts.flatMap((a) => a.chapters).filter((c) => !c.archived)) {
    const prefix = chapter.number === null ? '' : `Ch ${chapter.number} · `;
    options.push({
      value: `chapter:${chapter.id}`,
      label: `${prefix}${chapter.title}`,
      indent: false,
    });
    for (const scene of chapter.scenes) {
      options.push({
        value: `scene:${scene.id}`,
        label:
          `${chapter.number === null ? 'Sc' : `Sc ${scene.number}`} ${scene.title ?? ''}`.trim(),
        indent: true,
      });
    }
  }
  return options;
}

export function sceneInfos(outline: Outline): Map<string, SceneInfo> {
  const infos = new Map<string, SceneInfo>();
  for (const chapter of outline.acts.flatMap((a) => a.chapters)) {
    chapter.scenes.forEach((scene, i) => {
      const sceneNumber = scene.number.split('.').pop() ?? String(i + 1);
      infos.set(scene.id, {
        id: scene.id,
        number: scene.number,
        label: `${chapter.number === null ? 'Front' : `Ch ${chapter.number}`} · Sc ${sceneNumber}`,
      });
    });
  }
  return infos;
}

/** The scope a `#scene=5.2` or `#chapter=5` deep link names, or null. */
export function scopeFromAnchor(outline: Outline, scene?: string, chapter?: string): string | null {
  const chapters = outline.acts.flatMap((a) => a.chapters);
  if (scene !== undefined) {
    const found = chapters.flatMap((c) => c.scenes).find((s) => s.number === scene);
    if (found) return `scene:${found.id}`;
  }
  if (chapter !== undefined) {
    const found = chapters.find((c) => String(c.number) === chapter);
    if (found) return `chapter:${found.id}`;
  }
  return null;
}

/** The first chapter with prose to show when no deep link names one, else the whole book. */
export function defaultScope(outline: Outline): string {
  const first = outline.acts
    .flatMap((a) => a.chapters)
    .find((c) => !c.archived && c.scenes.length > 0);
  return first ? `chapter:${first.id}` : 'book';
}

/** The chapter a proof of `scope` covers: the chapter itself, a scene's chapter, or null for the whole book. */
export function proofChapter(outline: Outline, scope: string): string | null {
  const [kind, id] = scope.split(':');
  const chapters = outline.acts.flatMap((a) => a.chapters);
  if (kind === 'chapter') return chapters.some((c) => c.id === id) ? id : null;
  if (kind === 'scene') return chapters.find((c) => c.scenes.some((s) => s.id === id))?.id ?? null;
  return null;
}
