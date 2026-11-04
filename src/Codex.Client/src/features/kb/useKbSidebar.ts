import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';
import { buildKbSidebar } from '@codex/core/features/kb/kbSidebar';

const noneHidden: readonly string[] = [];

export function useKbSidebar(
  bookId: string,
  query: string,
  hiddenCategories: readonly string[] = noneHidden,
) {
  const result = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  const model = useMemo(
    () =>
      result.data
        ? buildKbSidebar(result.data, query, hiddenCategories)
        : { sections: [], totalEntries: 0, categories: [] },
    [result.data, query, hiddenCategories],
  );
  return {
    ...model,
    status: result.isError ? 'error' : result.isPending ? 'loading' : 'ready',
    error: result.error,
  } as const;
}
