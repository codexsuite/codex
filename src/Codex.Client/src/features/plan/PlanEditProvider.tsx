import { useQuery } from '@tanstack/react-query';
import { useMemo, useState, type ReactNode } from 'react';
import { getKb, kbKey, type Kb } from '@codex/core/api/kb';
import type { MentionEntry } from '../../prose';
import { PlanEditContext } from './planEditContext';

/** `@` picker entries: each entry with the name of its category and its aliases. */
export function mentionEntries(kb: Kb): MentionEntry[] {
  const categories = new Map(kb.categories.map((c) => [c.id, c.name]));
  return kb.entries.map((e) => ({
    id: e.id,
    name: e.name,
    category: categories.get(e.categoryId) ?? '',
    aliases: e.aliases,
  }));
}

/** The category colour of each entry; the accent when its category has no colour token. */
export function entryColours(kb: Kb): Map<string, string> {
  const categories = new Map(kb.categories.map((c) => [c.id, c.colour]));
  return new Map(
    kb.entries.map((e) => {
      const token = categories.get(e.categoryId);
      return [e.id, token ? `var(--${token}, var(--color-accent))` : 'var(--color-accent)'];
    }),
  );
}

// What every card on the board shares: the Knowledge base entries a summary can mention (the sidebar's query, so one
// request) and which new scene or act title should take focus.
export function PlanEditProvider({ bookId, children }: { bookId: string; children: ReactNode }) {
  const kb = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  const [focusSceneId, setFocusSceneId] = useState<string | null>(null);
  const [focusActId, setFocusActId] = useState<string | null>(null);
  const value = useMemo(
    () => ({
      entries: kb.data ? mentionEntries(kb.data) : [],
      colours: kb.data ? entryColours(kb.data) : new Map<string, string>(),
      focusSceneId,
      setFocusSceneId,
      focusActId,
      setFocusActId,
    }),
    [kb.data, focusSceneId, focusActId],
  );
  return <PlanEditContext.Provider value={value}>{children}</PlanEditContext.Provider>;
}
