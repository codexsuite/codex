import { useCallback, useMemo, useSyncExternalStore } from 'react';

// Two per-book view preferences kept in localStorage (the schema has no column for either):
//   codex.plan.collapsed.<book>  JSON { [actId]: boolean }
//   codex.plan.view.<book>       'board' | 'outline'
// Storage can be missing or throw (private windows); every access is guarded and behaves as if nothing was stored.

export type PlanView = 'board' | 'outline';

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

const collapsedKey = (bookId: string) => `codex.plan.collapsed.${bookId}`;
const viewKey = (bookId: string) => `codex.plan.view.${bookId}`;

function parseCollapsed(raw: string | null): Record<string, boolean> {
  if (raw === null) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, boolean>)
      : {};
  } catch {
    return {};
  }
}

/** The acts that start collapsed when nothing is stored for them: every act from the third on. */
const FIRST_COLLAPSED_INDEX = 2;

export function defaultCollapsed(actIndex: number): boolean {
  return actIndex >= FIRST_COLLAPSED_INDEX;
}

/** Whether each act is collapsed. An act the reader never toggled follows `defaultCollapsed`. */
export function useCollapsedActs(bookId: string) {
  const raw = useSyncExternalStore(subscribe, () => read(collapsedKey(bookId)));
  const stored = useMemo(() => parseCollapsed(raw), [raw]);
  const isCollapsed = useCallback(
    (actId: string, actIndex: number) => stored[actId] ?? defaultCollapsed(actIndex),
    [stored],
  );
  const setCollapsed = useCallback(
    (actId: string, collapsed: boolean) =>
      write(
        collapsedKey(bookId),
        JSON.stringify({ ...parseCollapsed(read(collapsedKey(bookId))), [actId]: collapsed }),
      ),
    [bookId],
  );
  /** Collapses or expands every given act at once (the toolbar's Collapse all / Expand all). */
  const setAllCollapsed = useCallback(
    (actIds: string[], collapsed: boolean) =>
      write(
        collapsedKey(bookId),
        JSON.stringify({
          ...parseCollapsed(read(collapsedKey(bookId))),
          ...Object.fromEntries(actIds.map((id) => [id, collapsed])),
        }),
      ),
    [bookId],
  );
  return { isCollapsed, setCollapsed, setAllCollapsed };
}

export function usePlanView(bookId: string) {
  const raw = useSyncExternalStore(subscribe, () => read(viewKey(bookId)));
  const view: PlanView = raw === 'outline' ? 'outline' : 'board';
  const setView = useCallback((next: PlanView) => write(viewKey(bookId), next), [bookId]);
  return [view, setView] as const;
}
