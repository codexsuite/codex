import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';
import type { MentionEntry } from '../../prose';

const NONE: MentionEntry[] = [];

// The book's entries as `@` picker choices: id, name, category name and aliases.
export function useKbEntries(bookId: string): MentionEntry[] {
  const { data } = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  return useMemo(() => {
    if (!data) return NONE;
    const names = new Map(data.categories.map((c) => [c.id, c.name]));
    return data.entries.map((e) => ({
      id: e.id,
      name: e.name,
      category: names.get(e.categoryId) ?? '',
      aliases: e.aliases,
    }));
  }, [data]);
}
