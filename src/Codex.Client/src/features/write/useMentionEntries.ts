import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';
import type { MentionEntry } from '../../prose';

// The book's Knowledge base entries as the `@` picker wants them; empty until the query has loaded.
const NONE: MentionEntry[] = [];

export function useMentionEntries(bookId: string): readonly MentionEntry[] {
  const kb = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  return useMemo(() => {
    if (!kb.data) return NONE;
    const categories = new Map(kb.data.categories.map((c) => [c.id, c.name]));
    return kb.data.entries.map((e) => ({
      id: e.id,
      name: e.name,
      category: categories.get(e.categoryId) ?? '',
      aliases: e.aliases,
    }));
  }, [kb.data]);
}
