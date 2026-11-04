import type { Outline, OutlineAct, OutlineChapter, OutlineScene } from '@codex/core/api/structure';

export function outlineScene(overrides: Partial<OutlineScene> = {}): OutlineScene {
  return {
    id: 'scene-1',
    number: '1.1',
    title: 'An Empty Seat',
    summary: 'The court waits for a queen who does not come.',
    status: 'draft',
    position: 'a0',
    words: 3_000,
    hasPendingDraft: false,
    labels: [],
    linkedEntries: [],
    ...overrides,
  };
}

export function outlineChapter(overrides: Partial<OutlineChapter> = {}): OutlineChapter {
  return {
    id: 'chapter-1',
    number: 1,
    title: 'An Empty Seat',
    slug: 'an-empty-seat',
    position: 'a0',
    archived: false,
    archivedSceneCount: 0,
    scenes: [outlineScene()],
    opener: null,
    ...overrides,
  };
}

export function outlineAct(overrides: Partial<OutlineAct> = {}): OutlineAct {
  return {
    id: 'act-1',
    title: 'The Court',
    position: 'a0',
    chapters: [outlineChapter()],
    opener: null,
    ...overrides,
  };
}

export function outline(overrides: Partial<Outline> = {}): Outline {
  return { bookId: 'book-1', acts: [outlineAct()], ...overrides };
}
