import { useCallback, useSyncExternalStore } from 'react';
import { useMe } from '../../api/authQueries';

// Which view each editor surface opens in, remembered per signed-in user and per surface in localStorage:
//   codex.editor.mode.<user id>.<surface>   'rich' | 'source'
// Storage can be missing or throw (private windows); every access is guarded and behaves as if nothing was stored.

export type EditorMode = 'rich' | 'source';

/** The Markdown surfaces that offer the toggle. */
export type SurfaceKey =
  | 'scene-prose'
  | 'kb-entry'
  | 'scene-summary'
  | 'beat-instruction'
  | 'kb-addition'
  | 'style-guide'
  | 'series-settings';

// Long-form instructions open in Source until the user chooses; everything else opens in Rich.
const DEFAULT_MODE: Partial<Record<SurfaceKey, EditorMode>> = {
  'style-guide': 'source',
  'series-settings': 'source',
};

const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => void listeners.delete(listener);
};

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // nothing stored; the next read says so
  }
  listeners.forEach((l) => l());
}

const modeKey = (userId: string, surface: SurfaceKey) => `codex.editor.mode.${userId}.${surface}`;

/** The view a surface opens in for the signed-in user (its default until they choose), and a setter that remembers it. */
export function useEditorMode(surface: SurfaceKey) {
  const userId = useMe().data?.id ?? 'anonymous';
  const raw = useSyncExternalStore(subscribe, () => read(modeKey(userId, surface)));
  const mode: EditorMode =
    raw === 'source' || raw === 'rich' ? raw : (DEFAULT_MODE[surface] ?? 'rich');
  const setMode = useCallback(
    (next: EditorMode) => write(modeKey(userId, surface), next),
    [userId, surface],
  );
  return [mode, setMode] as const;
}
