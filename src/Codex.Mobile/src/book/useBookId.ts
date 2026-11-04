import { useFocusEffect, useLocalSearchParams } from 'expo-router';
import { useCallback, useLayoutEffect } from 'react';
import { bookSaveStatus } from './saveStatus';

/**
 * The `[bookId]` of the route this screen is in (every screen under `app/book/[bookId]/`). Being on a book's screen also
 * makes it the book the save dot reports for, so an unsaved edit of the book you left does not colour this one. It is
 * set when the screen mounts and again whenever it gains focus, so going back to a book after another one was opened
 * on top of it restores that book's dot.
 */
export function useBookId(): string {
  const { bookId } = useLocalSearchParams<{ bookId: string }>();
  useLayoutEffect(() => {
    if (bookId) bookSaveStatus.activate(bookId);
  }, [bookId]);
  useFocusEffect(
    useCallback(() => {
      if (bookId) bookSaveStatus.activate(bookId);
    }, [bookId]),
  );
  return bookId;
}
