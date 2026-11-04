import type { Outline, OutlineAct, OutlineChapter, OutlineScene } from '../../api/structure';
import type { Manuscript } from '../../api/write';

// The Write screen's read model: the outline (structure, numbers, gutters) joined with the manuscript (prose) by
// scene id. Archived chapters are not part of the manuscript; an act with no live chapter draws no marker.

export interface SceneView {
  scene: OutlineScene;
  body: string;
  /** The prose version the body was loaded at (0 for a scene with no prose yet). */
  version: number;
  heading: string;
  wordsLabel: string;
}

export interface ChapterView {
  chapter: OutlineChapter;
  /** "Chapter 5", or null for an unnumbered chapter. */
  overline: string | null;
  countsLabel: string;
  /** What the copy button puts on the clipboard. */
  reference: string;
  scenes: SceneView[];
}

export interface ActView {
  act: OutlineAct;
  /** 1-based, counting every act of the book. */
  number: number;
  /** "Act 2 · Practices", or "Act 2" when the act has no title. */
  marker: string;
  chapters: ChapterView[];
}

export interface ChapterLinkLabel {
  text: string;
  missing: boolean;
  /** The chapter was deleted (archived) but is still in the book: the link shows its title and opens Plan's Archived list. */
  archived?: boolean;
  /** The chapter's current title, the link's tooltip. */
  title?: string;
}

const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;
const wordsText = (words: number) => `${words.toLocaleString('en-US')} words`;

export function buildManuscriptView(outline: Outline, manuscript: Manuscript): ActView[] {
  const prose = new Map(manuscript.scenes.map((s) => [s.sceneId, s]));
  return outline.acts.flatMap((act, actIndex) => {
    const chapters = act.chapters
      .filter((chapter) => !chapter.archived)
      .map((chapter): ChapterView => {
        const words = chapter.scenes.reduce((sum, s) => sum + s.words, 0);
        return {
          chapter,
          overline: chapter.number === null ? null : `Chapter ${chapter.number}`,
          countsLabel: `${plural(chapter.scenes.length, 'scene', 'scenes')} · ${wordsText(words)}`,
          reference: `ch:${chapter.slug}`,
          scenes: chapter.scenes.map((scene, index) => ({
            scene,
            body: prose.get(scene.id)?.body ?? '',
            version: prose.get(scene.id)?.version ?? 0,
            heading:
              chapter.number === null
                ? `Scene ${index + 1}`
                : `Chapter ${chapter.number} · Scene ${index + 1}`,
            wordsLabel: scene.words === 0 ? 'Not written' : wordsText(scene.words),
          })),
        };
      });
    return chapters.length === 0
      ? []
      : [
          {
            act,
            number: actIndex + 1,
            marker: act.title.trim() ? `Act ${actIndex + 1} · ${act.title}` : `Act ${actIndex + 1}`,
            chapters,
          },
        ];
  });
}

/** What each `[…](chapter:<id>)` link reads as: the chapter's live number, its title when archived, or "Missing chapter". */
export function chapterLinkLabels(outline: Outline): ReadonlyMap<string, ChapterLinkLabel> {
  const labels = new Map<string, ChapterLinkLabel>();
  for (const chapter of outline.acts.flatMap((a) => a.chapters)) {
    labels.set(
      chapter.id,
      chapter.archived
        ? { text: chapter.title, missing: false, archived: true, title: chapter.title }
        : {
            text: chapter.number === null ? `§ ${chapter.title}` : `§ Chapter ${chapter.number}`,
            missing: false,
            title: chapter.title,
          },
    );
  }
  return labels;
}

export const MISSING_CHAPTER: ChapterLinkLabel = { text: 'Missing chapter', missing: true };

export type ScrollTarget = { kind: 'scene' | 'chapter'; id: string };

/** `#scene=5.2` / `#chapter=5` resolved against the live outline; null when nothing matches. */
export function resolveTarget(
  acts: readonly ActView[],
  link: { scene?: string; chapter?: string },
): ScrollTarget | null {
  const chapters = acts.flatMap((a) => a.chapters);
  if (link.scene !== undefined) {
    const scene = chapters.flatMap((c) => c.scenes).find((s) => s.scene.number === link.scene);
    return scene ? { kind: 'scene', id: scene.scene.id } : null;
  }
  if (link.chapter !== undefined) {
    const chapter = chapters.find(
      (c) => c.chapter.number !== null && String(c.chapter.number) === link.chapter,
    );
    return chapter ? { kind: 'chapter', id: chapter.chapter.id } : null;
  }
  return null;
}
