import type { KbEntrySummary } from '@codex/core/api/kb';

/** The entries still offered to a scene: not already linked, matching the typed text on name or alias, by name. */
export function offeredEntries(
  entries: readonly KbEntrySummary[],
  linkedIds: readonly string[],
  query: string,
): KbEntrySummary[] {
  const text = query.trim().toLowerCase();
  return entries
    .filter((entry) => !linkedIds.includes(entry.id))
    .filter(
      (entry) =>
        text === '' ||
        entry.name.toLowerCase().includes(text) ||
        entry.aliases.some((alias) => alias.toLowerCase().includes(text)),
    )
    .sort((a, b) => a.name.localeCompare(b.name));
}
