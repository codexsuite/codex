import type { Manuscript, ManuscriptScene } from '@codex/core/api/write';

export function manuscriptScene(overrides: Partial<ManuscriptScene> = {}): ManuscriptScene {
  return { sceneId: 'scene-1', body: 'The court waited.\n', version: 1, ...overrides };
}

/** A chapter's prose in reading order, matching `outline()`'s one scene by default. */
export function manuscript(overrides: Partial<Manuscript> = {}): Manuscript {
  return { bookId: 'book-1', scenes: [manuscriptScene()], ...overrides };
}
