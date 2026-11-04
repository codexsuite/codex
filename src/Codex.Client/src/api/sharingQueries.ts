import { useQuery } from '@tanstack/react-query';
import { getSharing, sharingKey } from '@codex/core/api/sharing';

/** `GET /api/books/{id}/sharing`: the owner, the book's grants and its series' grants (emails for the owner only). */
export function useSharing(bookId: string) {
  return useQuery({
    queryKey: sharingKey(bookId),
    queryFn: ({ signal }) => getSharing(bookId, signal),
  });
}
