import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';
import { kbEntryKey, moveKbEntry } from '@codex/core/api/kbEntries';
import { useCanWrite } from '../../shell/bookAccess';

// Alt+ArrowUp / Alt+ArrowDown move the open entry one place within its category in the sidebar's order.
export function useEntryReorder(bookId: string, entryId: string) {
  const queryClient = useQueryClient();
  const { data } = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });

  // Below write (#80) nothing moves.
  const canWrite = useCanWrite();
  useEffect(() => {
    if (!canWrite) return;
    const onKey = (event: KeyboardEvent) => {
      if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
      const self = data?.entries.find((e) => e.id === entryId);
      if (!data || !self) return;
      const siblings = data.entries.filter(
        (e) => e.categoryId === self.categoryId && e.fromSeries === self.fromSeries,
      );
      const index = siblings.findIndex((e) => e.id === entryId);
      const up = event.key === 'ArrowUp';
      if (up ? index <= 0 : index >= siblings.length - 1) return;
      event.preventDefault();
      const afterId = up ? (siblings[index - 2]?.id ?? null) : siblings[index + 1].id;
      void moveKbEntry(entryId, { afterId }).then(() =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: kbKey(bookId) }),
          queryClient.invalidateQueries({ queryKey: kbEntryKey(entryId) }),
        ]),
      );
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [bookId, data, entryId, queryClient, canWrite]);
}
