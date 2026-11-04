import { createContext } from 'react';
import type { MentionEntry } from '../../prose';

export interface PlanEdit {
  /** Entries the `@` picker offers in a summary. */
  entries: MentionEntry[];
  /** Each entry's category colour as a CSS value (`var(--category-*)`), by entry id. */
  colours: ReadonlyMap<string, string>;
  /** The scene whose summary should take focus once it is on screen (just created). */
  focusSceneId: string | null;
  setFocusSceneId: (sceneId: string | null) => void;
  /** The act whose title should take focus once it is on screen (just created, or ✎ Rename act). */
  focusActId: string | null;
  setFocusActId: (actId: string | null) => void;
}

export const PlanEditContext = createContext<PlanEdit>({
  entries: [],
  colours: new Map(),
  focusSceneId: null,
  setFocusSceneId: () => undefined,
  focusActId: null,
  setFocusActId: () => undefined,
});
