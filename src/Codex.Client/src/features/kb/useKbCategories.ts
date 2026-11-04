import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey, type KbCategory } from '@codex/core/api/kb';

const NONE: KbCategory[] = [];

// The book's Knowledge base categories from the one shared query.
export function useKbCategories(bookId: string): KbCategory[] {
  const { data } = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  return data?.categories ?? NONE;
}
