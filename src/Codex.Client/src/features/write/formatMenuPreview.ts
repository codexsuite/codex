import type { ActView } from '@codex/core/features/write/manuscriptView';

/** What the Layout link carries for the scope in view: its scene when it is one scene, else its chapter when it is one chapter. */
export function previewAnchor(inScope: readonly ActView[]): { scene?: string; chapter?: string } {
  const chapters = inScope.flatMap((act) => act.chapters);
  if (chapters.length !== 1) return {};
  const [{ chapter, scenes }] = chapters;
  if (scenes.length === 1) return { scene: scenes[0].scene.number };
  return chapter.number === null ? {} : { chapter: String(chapter.number) };
}
