import { useBook } from '../../api/booksQueries';
import { hasRole, parseBookRole, type BookRole } from '@codex/core/api/bookRole';

export interface SettingsAccess {
  role: BookRole;
  /** False until the header has said who the caller is: nothing editable before then. */
  known: boolean;
  isOwner: boolean;
  /** Write or owner: the page is editable (owner-only actions aside). */
  canEdit: boolean;
  ownerName: string;
}

/**
 * What the signed-in user may do on this book's settings (#80), from the header the book bar already loads (no second
 * request). Viewers and commenters read the page; editors change it except for the owner-only actions.
 */
export function useSettingsAccess(bookId: string): SettingsAccess {
  const { data } = useBook(bookId);
  const role: BookRole = data ? parseBookRole(data.role) : 'view';
  return {
    role,
    known: data !== undefined,
    isOwner: role === 'owner',
    canEdit: hasRole(role, 'write'),
    ownerName: data?.owner.displayName ?? '',
  };
}
