/** A Knowledge base entry the `@` picker can mention. */
export interface MentionEntry {
  id: string;
  name: string;
  category: string;
  aliases: string[];
}

const PICKER_LIMIT = 8;

/** Entries whose name or an alias contains the query, names that start with it first. */
export function filterEntries(entries: readonly MentionEntry[], query: string): MentionEntry[] {
  const q = query.trim().toLowerCase();
  if (q === '') return entries.slice(0, PICKER_LIMIT);
  const rank = (entry: MentionEntry): number => {
    const names = [entry.name, ...entry.aliases].map((n) => n.toLowerCase());
    if (names.some((n) => n.startsWith(q))) return 0;
    return names.some((n) => n.includes(q)) ? 1 : 2;
  };
  return entries
    .map((entry) => ({ entry, rank: rank(entry) }))
    .filter((r) => r.rank < 2)
    .sort((a, b) => a.rank - b.rank)
    .slice(0, PICKER_LIMIT)
    .map((r) => r.entry);
}
