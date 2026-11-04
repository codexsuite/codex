import { platform } from '@codex/core/platform';

// Which acts of a book are collapsed, kept in the platform store under the desktop's key
// (`codex.plan.collapsed.<book>`, JSON { [actId]: boolean }). Storage can throw: every access is guarded and behaves
// as if nothing was stored.

const key = (bookId: string) => `codex.plan.collapsed.${bookId}`;

/** An act the reader never toggled starts collapsed from the third act on (as on desktop). */
export const FIRST_COLLAPSED_INDEX = 2;

export const defaultCollapsed = (actIndex: number) => actIndex >= FIRST_COLLAPSED_INDEX;

export function parseCollapsed(raw: string | null): Record<string, boolean> {
  if (raw === null) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {};
    return Object.fromEntries(
      Object.entries(parsed).filter(
        (entry): entry is [string, boolean] => typeof entry[1] === 'boolean',
      ),
    );
  } catch {
    return {};
  }
}

export function readCollapsed(bookId: string): Record<string, boolean> {
  try {
    return parseCollapsed(platform().persistentStore().getItem(key(bookId)));
  } catch {
    return {};
  }
}

export function writeCollapsed(bookId: string, collapsed: Record<string, boolean>): void {
  try {
    platform().persistentStore().setItem(key(bookId), JSON.stringify(collapsed));
  } catch {
    // nothing stored; the next read says so
  }
}

export function isActCollapsed(stored: Record<string, boolean>, actId: string, actIndex: number) {
  return stored[actId] ?? defaultCollapsed(actIndex);
}
