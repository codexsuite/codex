import { useQuery } from '@tanstack/react-query';
import { bookKey, getBook } from '@codex/core/api/books';
import { parseBookRole, type BookRole } from '@codex/core/api/bookRole';

/** `GET /api/books/{id}`: the header every book screen shows. A 404 surfaces as ApiRequestError. */
export function useBook(bookId: string) {
  return useQuery({
    queryKey: bookKey(bookId),
    queryFn: ({ signal }) => getBook(bookId, signal),
  });
}

/**
 * The signed-in user's role on the book (#80), from the header `useBook` already loads (one request per screen).
 * `view` until the header arrives, so nothing is editable before the server has said it may be. Below `write` a screen
 * is read-only; `hasRole(role, 'write')` is the check.
 */
export function useBookRole(bookId: string): BookRole {
  const { data } = useBook(bookId);
  return data ? parseBookRole(data.role) : 'view';
}
