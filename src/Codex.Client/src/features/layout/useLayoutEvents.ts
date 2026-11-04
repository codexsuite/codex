import { useEffect, useRef } from 'react';
import { subscribeToBook, type SubscribeOptions } from '../../realtime/bookSubscription';

const SETTLE_MS = 250;

// Calls `onProse` once the book's prose has been saved (by this tab or another process) and nothing more arrived for
// a moment, with the scene of the last save (null after a structure change). Its own hub connection while Layout is open.
export function useLayoutEvents(
  bookId: string,
  onProse: (sceneId: string | null) => void,
  options?: SubscribeOptions,
): void {
  const handler = useRef(onProse);
  useEffect(() => {
    handler.current = onProse;
  });
  useEffect(() => {
    if (bookId === '') return;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const stop = subscribeToBook(
      bookId,
      (event) => {
        if (event.type !== 'proseSaved' && event.type !== 'structureChanged') return;
        const sceneId = event.type === 'proseSaved' ? event.sceneId : null;
        clearTimeout(timer);
        timer = setTimeout(() => handler.current(sceneId), SETTLE_MS);
      },
      options,
    );
    return () => {
      clearTimeout(timer);
      stop();
    };
  }, [bookId, options]);
}
