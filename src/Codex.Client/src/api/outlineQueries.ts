import { useQuery } from '@tanstack/react-query';
import { outlineKey } from '@codex/core/api/outline';
import { structureApi } from '@codex/core/api/structure';

/** `GET /api/books/{id}/outline`. A 404 surfaces as ApiRequestError. */
export function useOutline(bookId: string) {
  return useQuery({
    queryKey: outlineKey(bookId),
    queryFn: ({ signal }) => structureApi.outline(bookId, { signal }),
  });
}
