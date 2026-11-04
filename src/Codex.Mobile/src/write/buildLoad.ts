import type { Kb } from '@codex/core/api/kb';
import type { Outline, OutlineScene } from '@codex/core/api/structure';
import type { BookBeats, BookKbAdditions, Manuscript } from '@codex/core/api/write';
import {
  EDITOR_BRIDGE_VERSION,
  type BridgeDensity,
  type BridgeEntry,
  type BridgeScene,
  type BridgeTheme,
  type HostMessage,
} from '@codex/core/editing/editorBridge';
import type { WriteChapter } from './chapters';

// Builds what the editor page is sent from the cached reads. Pure: the screen reads the query cache and calls these.

/** The reads Write needs for a chapter. */
export interface WriteData {
  outline: Outline;
  manuscript: Manuscript;
  kb: Kb;
  beats: BookBeats;
  additions: BookKbAdditions;
}

export function buildEntries(kb: Kb): BridgeEntry[] {
  const categoryNames = new Map(kb.categories.map((c) => [c.id, c.name]));
  return kb.entries.map((entry) => ({
    id: entry.id,
    name: entry.name,
    category: categoryNames.get(entry.categoryId) ?? '',
    aliases: entry.aliases,
  }));
}

/** One scene as the page draws it: card, beats, additions and the stored prose at its saved version. */
export function buildScene(scene: OutlineScene, data: WriteData): BridgeScene {
  const categoryOf = new Map(data.kb.categories.map((c) => [c.id, c.name]));
  const entryOf = new Map(data.kb.entries.map((e) => [e.id, e]));
  const prose = data.manuscript.scenes.find((s) => s.sceneId === scene.id);
  return {
    id: scene.id,
    number: scene.number,
    title: scene.title ?? '',
    labels: scene.labels.map((label) => label.name),
    words: scene.words,
    summary: scene.summary ?? '',
    chips: scene.linkedEntries.map((ref) => ({
      id: ref.id,
      name: ref.name,
      category: categoryOf.get(entryOf.get(ref.id)?.categoryId ?? '') ?? '',
    })),
    beats: data.beats.beats
      .filter((beat) => beat.sceneId === scene.id)
      .map((beat) => ({
        id: beat.id,
        title: beat.title,
        body: beat.body,
        targetWordCount: beat.targetWordCount,
        hidden: beat.isHidden,
      })),
    additions: data.additions.additions
      .filter((addition) => addition.sceneId === scene.id)
      .map((addition) => ({
        id: addition.id,
        entryId: addition.entryId,
        entryName: entryOf.get(addition.entryId)?.name ?? '',
        body: addition.body,
      })),
    prose: prose?.body ?? '',
    version: prose?.version ?? 0,
  };
}

export interface LoadOptions {
  theme: BridgeTheme;
  density: BridgeDensity;
  readOnly: boolean;
  scrollToSceneId: string | null;
}

export function buildLoad(
  chapter: WriteChapter,
  data: WriteData,
  options: LoadOptions,
): Extract<HostMessage, { type: 'load' }> {
  return {
    type: 'load',
    v: EDITOR_BRIDGE_VERSION,
    theme: options.theme,
    density: options.density,
    scenes: chapter.scenes.map((scene) => buildScene(scene, data)),
    entries: buildEntries(data.kb),
    scrollToSceneId: options.scrollToSceneId,
    readOnly: options.readOnly,
  };
}

/** The card details of a scene as a `sceneMeta` message (the scene sheet changed them; prose and cursor stay). */
export function sceneMetaOf(scene: BridgeScene): Extract<HostMessage, { type: 'sceneMeta' }> {
  return {
    type: 'sceneMeta',
    sceneId: scene.id,
    title: scene.title,
    labels: scene.labels,
    words: scene.words,
    summary: scene.summary,
    chips: scene.chips,
  };
}
