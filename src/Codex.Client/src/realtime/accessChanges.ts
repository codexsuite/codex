import type { QueryClient } from '@tanstack/react-query';
import { meKey, type Me } from '@codex/core/api/auth';
import { hasRole, parseBookRole } from '@codex/core/api/bookRole';
import { bookKey } from '@codex/core/api/books';
import type { AccessChanged } from '@codex/core/api/events';
import type { BookHeader } from '@codex/core/api/types';
import type { showToast } from '@codex/core/editing/toastStore';

export interface AccessChangeDeps {
  queryClient: QueryClient;
  toast: typeof showToast;
  /** Back to the Library: this user can no longer open the book. */
  leave: () => void;
}

const roleNames = { view: 'Can view', comment: 'Can comment', write: 'Can edit' } as const;

// AccessChanged (#80): the owner changed or removed this user's access to the open book. The header in the cache takes
// the new role straight away, so every screen re-renders at it without a reload (and is refetched to be sure), and the
// Library re-reads its list. A removed access goes back to the Library. The toasts are the design's
// (design/screens/write.html, setRole).
export function applyAccessChange(event: AccessChanged, deps: AccessChangeDeps): void {
  const { queryClient, toast } = deps;
  // The hub sends it to this user's connections only; one about someone else (another account signed in on the same
  // browser since) is not this tab's business.
  const me = queryClient.getQueryData<Me>(meKey);
  if (me !== undefined && me.id.toLowerCase() !== event.userId.toLowerCase()) return;

  const key = bookKey(event.bookId);
  const header = queryClient.getQueryData<BookHeader>(key);
  void queryClient.invalidateQueries({ queryKey: ['library'] });
  const owner = header?.owner.displayName ?? 'The owner';

  if (event.role === null) {
    // Stale, but not refetched while this screen is still up (it would 404 into the error state on the way out).
    void queryClient.invalidateQueries({ queryKey: key, exact: true, refetchType: 'none' });
    deps.leave();
    toast({
      message: `${owner} stopped sharing ${header ? `“${header.title}”` : 'this book'} with you.`,
    });
    return;
  }

  const wasWritable = header !== undefined && hasRole(parseBookRole(header.role), 'write');
  const role = parseBookRole(event.role);
  if (header !== undefined) queryClient.setQueryData<BookHeader>(key, { ...header, role });
  void queryClient.invalidateQueries({ queryKey: key, exact: true });
  if (hasRole(role, 'write')) {
    toast({
      message: wasWritable
        ? `Your access changed to ${roleNames.write}`
        : 'You can edit this book now. Typing works again.',
    });
  } else if (role === 'view' || role === 'comment') {
    toast({
      message: `${owner} changed your access to ${roleNames[role]}. This tab is read-only now.`,
    });
  }
}
