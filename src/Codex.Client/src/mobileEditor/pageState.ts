import type {
  BridgeChip,
  BridgeDensity,
  BridgeEntry,
  BridgeScene,
  BridgeTheme,
} from '@codex/core/editing/editorBridge';

// What the page knows: the last `load`, kept up to date by `setScene` and `sceneMeta`. The prose the reader types is not
// here (the editors own it and report it with `change`); `scenes[i].prose` is what the scene was last given by the app.

export interface PageState {
  /** Bumped by each `load`; scene editors are keyed by it, so a new `load` rebuilds them. */
  epoch: number;
  loaded: boolean;
  theme: BridgeTheme;
  density: BridgeDensity;
  readOnly: boolean;
  scenes: BridgeScene[];
  entries: BridgeEntry[];
  /** The scene whose card is open (the one in view while reading); the other cards collapse. */
  activeSceneId: string | null;
  /** A scene has focus: the cards stay as they were when it got focus, so nothing moves under the caret. */
  editing: boolean;
  /** Per scene, how many times `setScene` has replaced it: the editor re-reads the prose when this moves. */
  replaced: Record<string, number>;
  /** Where to scroll once the `load` is drawn. */
  scrollToSceneId: string | null;
}

export type PageAction =
  | {
      type: 'load';
      theme: BridgeTheme;
      density: BridgeDensity;
      readOnly: boolean;
      scenes: BridgeScene[];
      entries: BridgeEntry[];
      scrollToSceneId: string | null;
    }
  | { type: 'setScene'; scene: BridgeScene }
  | {
      type: 'sceneMeta';
      sceneId: string;
      title: string;
      labels: string[];
      words: number;
      summary: string;
      chips: BridgeChip[];
    }
  /** The topmost scene in view while reading; ignored while editing. */
  | { type: 'reading'; sceneId: string }
  | { type: 'editing'; editing: boolean };

export const initialPageState: PageState = {
  epoch: 0,
  loaded: false,
  theme: 'light',
  density: 'touch',
  readOnly: false,
  scenes: [],
  entries: [],
  activeSceneId: null,
  editing: false,
  replaced: {},
  scrollToSceneId: null,
};

export function pageReducer(state: PageState, action: PageAction): PageState {
  switch (action.type) {
    case 'load': {
      const target = action.scrollToSceneId;
      const known = target !== null && action.scenes.some((s) => s.id === target);
      return {
        epoch: state.epoch + 1,
        loaded: true,
        theme: action.theme,
        density: action.density,
        readOnly: action.readOnly,
        scenes: action.scenes,
        entries: action.entries,
        activeSceneId: known ? target : (action.scenes[0]?.id ?? null),
        editing: false,
        replaced: {},
        scrollToSceneId: known ? target : null,
      };
    }
    case 'setScene': {
      const { scene } = action;
      if (!state.scenes.some((s) => s.id === scene.id)) return state;
      return {
        ...state,
        scenes: state.scenes.map((s) => (s.id === scene.id ? scene : s)),
        replaced: { ...state.replaced, [scene.id]: (state.replaced[scene.id] ?? 0) + 1 },
      };
    }
    case 'sceneMeta': {
      const { sceneId, title, labels, words, summary, chips } = action;
      return {
        ...state,
        scenes: state.scenes.map((s) =>
          s.id === sceneId ? { ...s, title, labels, words, summary, chips } : s,
        ),
      };
    }
    case 'reading':
      return state.editing || state.activeSceneId === action.sceneId
        ? state
        : { ...state, activeSceneId: action.sceneId };
    case 'editing':
      return state.editing === action.editing ? state : { ...state, editing: action.editing };
  }
}
