import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';

/** What Write draws an entry with: its names, and the category colour as a CSS value. */
export interface KbInfo {
  id: string;
  name: string;
  category: string;
  /** `var(--category-*)`, or the accent when the category has no colour token. */
  colour: string;
  aliases: readonly string[];
}

const NONE: ReadonlyMap<string, KbInfo> = new Map();

export const categoryColour = (token: string | null): string =>
  token ? `var(--${token}, var(--color-accent))` : 'var(--color-accent)';

// The book's entries by id, from the same query as the `@` picker.
export function useKbInfo(bookId: string): ReadonlyMap<string, KbInfo> {
  const kb = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  return useMemo(() => {
    if (!kb.data) return NONE;
    const categories = new Map(kb.data.categories.map((c) => [c.id, c]));
    return new Map(
      kb.data.entries.map((e) => {
        const category = categories.get(e.categoryId);
        return [
          e.id,
          {
            id: e.id,
            name: e.name,
            category: category?.name ?? '',
            colour: categoryColour(category?.colour ?? null),
            aliases: e.aliases,
          },
        ];
      }),
    );
  }, [kb.data]);
}

export interface NameMatch {
  entryId: string;
  from: number;
  to: number;
}

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Finds whole-word, case-insensitive occurrences of any entry's name or alias in a piece of text. */
export function nameMatcher(infos: ReadonlyMap<string, KbInfo>): (text: string) => NameMatch[] {
  const owner = new Map<string, string>();
  for (const info of infos.values()) {
    for (const name of [info.name, ...info.aliases]) {
      const key = name.trim().toLowerCase();
      if (key.length >= 2 && !owner.has(key)) owner.set(key, info.id);
    }
  }
  if (owner.size === 0) return () => [];
  const names = [...owner.keys()].sort((a, b) => b.length - a.length);
  const pattern = new RegExp(
    `(?<![\\p{L}\\p{N}_])(?:${names.map(escape).join('|')})(?![\\p{L}\\p{N}_])`,
    'giu',
  );
  return (text) => {
    const found: NameMatch[] = [];
    for (const m of text.matchAll(pattern)) {
      const entryId = owner.get(m[0].toLowerCase());
      if (entryId) found.push({ entryId, from: m.index, to: m.index + m[0].length });
    }
    return found;
  };
}
