import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { bookKey } from '@codex/core/api/books';
import { libraryLink } from '@codex/core/shell/links';
import { subscribeToBook, type SubscribeOptions } from './bookSubscription';
import { isForbidden, onForbidden } from '@codex/core/realtime/forbidden';
import { RemoteChanges } from './remoteChanges';

// Mounted once by the book workspace: follows the open book's events for as long as it is open, and starts over when
// the book changes. The options are for tests.
export function useBookRealtime(bookId: string, options?: SubscribeOptions): void {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const leave = useRef(() => void navigate(libraryLink()));
  useEffect(() => {
    leave.current = () => void navigate(libraryLink());
  }, [navigate]);
  useEffect(() => {
    if (bookId === '') return;
    const changes = new RemoteChanges(bookId, { queryClient, leave: () => leave.current() });
    const stop = subscribeToBook(bookId, (event) => changes.handle(event), options);
    // A save refused as forbidden: the role may have dropped before AccessChanged arrived; re-read the header.
    // Mutations report it through the cache; autosaves and other direct calls through reportForbidden.
    const reread = () =>
      void queryClient.invalidateQueries({ queryKey: bookKey(bookId), exact: true });
    const stopForbidden = onForbidden(reread);
    const stopMutations = queryClient.getMutationCache().subscribe((event) => {
      if (
        event.type === 'updated' &&
        event.action.type === 'error' &&
        isForbidden(event.action.error)
      )
        reread();
    });
    return () => {
      stop();
      stopForbidden();
      stopMutations();
      changes.dispose();
    };
  }, [bookId, queryClient, options]);
}
