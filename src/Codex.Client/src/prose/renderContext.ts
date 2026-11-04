import { createContext, type ReactNode } from 'react';

/** What a leaf directive line (`::beat{id}` …) carries. `id` is the `id`/`kb` attribute, null for `::pagebreak`. */
export interface DirectiveInfo {
  name: string;
  id: string | null;
}

export interface ProseRenderHooks {
  /** Replaces the default card for a leaf directive. Return undefined to keep the default. */
  renderDirective?: (directive: DirectiveInfo) => ReactNode | undefined;
}

export const ProseRenderContext = createContext<ProseRenderHooks>({});
