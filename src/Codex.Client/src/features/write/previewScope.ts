import type { LayoutPreviewRequest } from '@codex/core/api/layout';
import type { ActView, ChapterView } from '@codex/core/features/write/manuscriptView';
import type { Scope } from '@codex/core/features/write/scope';

export interface PreviewTarget {
  request: LayoutPreviewRequest;
  /** The scene or chapter, as the Layout link and the pane's header name it. */
  anchor: { scene?: string; chapter?: string };
  label: string;
  /** The prose versions of the scenes previewed: a save changes it, and the preview is made again. */
  versions: string;
}

/**
 * What the Preview pane shows for Write's scope: the scene or the chapter in scope; for an act or the whole book, the
 * chapter being read (`readingChapterId`), else the first chapter in view. Null when nothing is in view.
 */
export function previewTarget(
  scope: Scope,
  inScope: readonly ActView[],
  readingChapterId: string | null,
): PreviewTarget | null {
  const chapters = inScope.flatMap((act) => act.chapters);
  if (scope.kind === 'scene') {
    const chapter = chapters.find((c) => c.scenes.some((s) => s.scene.id === scope.id));
    const scene = chapter?.scenes.find((s) => s.scene.id === scope.id);
    if (scene) {
      return {
        request: { scope: 'scene', scopeId: scene.scene.id },
        anchor: { scene: scene.scene.number },
        label: scene.heading,
        versions: `${scene.scene.id}@${scene.version}`,
      };
    }
  }
  const chapter =
    chapters.find((c) => c.chapter.id === readingChapterId) ??
    chapters.find((c) => c.scenes.length > 0) ??
    chapters[0];
  return chapter ? chapterTarget(chapter) : null;
}

function chapterTarget(view: ChapterView): PreviewTarget {
  const { chapter } = view;
  return {
    request: { scope: 'chapter', scopeId: chapter.id },
    anchor: chapter.number === null ? {} : { chapter: String(chapter.number) },
    label: view.overline ? `${view.overline}: ${chapter.title}` : chapter.title,
    versions: view.scenes.map((s) => `${s.scene.id}@${s.version}`).join(','),
  };
}
