import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { referencesLink } from '@codex/core/shell/links';
import { refreshClearance } from '../refreshClearance';

/** What follows a write that changed the book's sources: refresh the list, strip and badge, then open a source's panel. */
export function useAddedSource(bookId: string) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const refresh = () => refreshClearance(queryClient, bookId);
  const open = (sourceId: string) =>
    void navigate(referencesLink(bookId, { source: sourceId }), { replace: true });
  return { refresh, open };
}
