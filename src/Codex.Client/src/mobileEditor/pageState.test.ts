import { describe, expect, it } from 'vitest';
import type { BridgeScene } from '@codex/core/editing/editorBridge';
import { initialPageState, pageReducer, type PageAction, type PageState } from './pageState';

const scene = (id: string, over: Partial<BridgeScene> = {}): BridgeScene => ({
  id,
  number: '1.1',
  title: `Title ${id}`,
  labels: [],
  words: 0,
  summary: '',
  chips: [],
  beats: [],
  additions: [],
  prose: '',
  version: 1,
  ...over,
});

const load = (over: Partial<Extract<PageAction, { type: 'load' }>> = {}): PageAction => ({
  type: 'load',
  theme: 'dark',
  density: 'touch',
  readOnly: false,
  scenes: [scene('a'), scene('b')],
  entries: [],
  scrollToSceneId: null,
  ...over,
});

const loaded = (over: Partial<Extract<PageAction, { type: 'load' }>> = {}): PageState =>
  pageReducer(initialPageState, load(over));

describe('pageReducer load', () => {
  it('load_WhenFirst_ShouldOpenTheFirstSceneAndCountTheLoad', () => {
    const state = loaded();

    expect(state).toMatchObject({ loaded: true, epoch: 1, activeSceneId: 'a', theme: 'dark' });
    expect(state.scrollToSceneId).toBeNull();
  });

  it('load_WhenAskedToScrollToAScene_ShouldOpenAndScrollToIt', () => {
    const state = loaded({ scrollToSceneId: 'b' });

    expect(state).toMatchObject({ activeSceneId: 'b', scrollToSceneId: 'b' });
  });

  it('load_WhenTheScrollTargetIsNotInTheChapter_ShouldFallBackToTheFirstScene', () => {
    const state = loaded({ scrollToSceneId: 'gone' });

    expect(state).toMatchObject({ activeSceneId: 'a', scrollToSceneId: null });
  });

  it('load_WhenSecond_ShouldReplaceEverythingAndBumpTheEpoch', () => {
    const first = pageReducer(loaded(), { type: 'setScene', scene: scene('a', { prose: 'x' }) });

    const second = pageReducer(first, load({ scenes: [scene('c')] }));

    expect(second.epoch).toBe(2);
    expect(second.scenes.map((s) => s.id)).toEqual(['c']);
    expect(second.replaced).toEqual({});
  });

  it('load_WhenTheChapterHasNoScenes_ShouldHaveNoActiveScene', () => {
    expect(loaded({ scenes: [] }).activeSceneId).toBeNull();
  });
});

describe('pageReducer setScene', () => {
  it('setScene_WhenTheSceneIsKnown_ShouldReplaceItAndCountTheReplacement', () => {
    const state = pageReducer(loaded(), {
      type: 'setScene',
      scene: scene('b', { prose: 'new', version: 4 }),
    });

    expect(state.scenes[1]).toMatchObject({ id: 'b', prose: 'new', version: 4 });
    expect(state.scenes[0]!.prose).toBe('');
    expect(state.replaced).toEqual({ b: 1 });
  });

  it('setScene_WhenTheSceneIsUnknown_ShouldChangeNothing', () => {
    const before = loaded();

    expect(pageReducer(before, { type: 'setScene', scene: scene('zzz') })).toBe(before);
  });
});

describe('pageReducer sceneMeta', () => {
  it('sceneMeta_WhenApplied_ShouldChangeTheCardOnlyAndNotCountAReplacement', () => {
    const state = pageReducer(loaded(), {
      type: 'sceneMeta',
      sceneId: 'a',
      title: 'Renamed',
      labels: ['Drafting'],
      words: 12,
      summary: 'S',
      chips: [{ id: 'k', name: 'Maren', category: 'person' }],
    });

    expect(state.scenes[0]).toMatchObject({ title: 'Renamed', labels: ['Drafting'], words: 12 });
    expect(state.scenes[0]!.prose).toBe('');
    expect(state.replaced).toEqual({});
  });
});

describe('pageReducer reading and editing', () => {
  it('reading_WhenAnotherSceneIsInView_ShouldOpenItsCard', () => {
    expect(pageReducer(loaded(), { type: 'reading', sceneId: 'b' }).activeSceneId).toBe('b');
  });

  it('reading_WhenAlreadyOpen_ShouldReturnTheSameState', () => {
    const before = loaded();

    expect(pageReducer(before, { type: 'reading', sceneId: 'a' })).toBe(before);
  });

  it('reading_WhenASceneHasFocus_ShouldKeepTheCardsAsTheyWere', () => {
    const editing = pageReducer(loaded(), { type: 'editing', editing: true });

    expect(pageReducer(editing, { type: 'reading', sceneId: 'b' }).activeSceneId).toBe('a');
  });

  it('editing_WhenFocusLeaves_ShouldLetReadingMoveTheCardsAgain', () => {
    const editing = pageReducer(loaded(), { type: 'editing', editing: true });
    const left = pageReducer(editing, { type: 'editing', editing: false });

    expect(pageReducer(left, { type: 'reading', sceneId: 'b' }).activeSceneId).toBe('b');
  });

  it('load_WhenASceneHadFocus_ShouldStopEditing', () => {
    const editing = pageReducer(loaded(), { type: 'editing', editing: true });

    expect(pageReducer(editing, load()).editing).toBe(false);
  });
});
