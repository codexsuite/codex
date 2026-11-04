import { createContext, useContext } from 'react';
import { hasRole, type BookRole } from '@codex/core/api/bookRole';

// The signed-in user's access to the open book (#80), from the header the workspace already loads (`useBookRole`), so
// no screen fetches it again. Below `write` every screen is read-only: the UI hides or disables what the server would
// refuse (the server is the guard). Outside a book workspace (a component harness) nothing is restricted.
export interface BookAccess {
  role: BookRole;
  /** `hasRole(role, 'write')`: prose, plan, KB, references, layout and figures may change. */
  canWrite: boolean;
  /** Who shared the book; the role chip's tooltip names them. */
  ownerName: string;
}

export const BookAccessContext = createContext<BookAccess>({
  role: 'owner',
  canWrite: true,
  ownerName: '',
});

export function bookAccess(role: BookRole, ownerName: string): BookAccess {
  return { role, canWrite: hasRole(role, 'write'), ownerName };
}

export function useBookAccess(): BookAccess {
  return useContext(BookAccessContext);
}

/** False below write: the screen reads normally but takes no edits. */
export function useCanWrite(): boolean {
  return useContext(BookAccessContext).canWrite;
}
