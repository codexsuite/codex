import { hasRole, parseBookRole } from '@codex/core/api/bookRole';
import { useBookHeader } from '~/book/queries';

/** Whether the caller may change the book's Knowledge base (`write` or above). False until the header has loaded. */
export function useCanWrite(bookId: string): boolean {
  const role = useBookHeader(bookId).data?.role;
  return role !== undefined && hasRole(parseBookRole(role), 'write');
}
