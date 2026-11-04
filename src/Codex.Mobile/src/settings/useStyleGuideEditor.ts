import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import { settingsApi, settingsKey } from '@codex/core/api/settings';
import { useAutosaveController } from '~/book/useAutosaveController';

const ignore = () => undefined;

/**
 * The style guide text with the desktop's autosave (3 s after the first edit, on demand for Done and back), reporting
 * to its own source of the book's save status (`~/book/saveStatus`). A failed save keeps the text and shows the
 * server's message. Leaving waits for a save already in flight, then sends what is left; an edit that could not go
 * because the phone is offline stays queued in the controller (which is then not disposed) and goes when the
 * connection is back.
 */
export function useStyleGuideEditor(bookId: string, saved: string) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const failed = useRef(false);
  const inflight = useRef<Promise<unknown> | null>(null);

  const send = async (value: string) => {
    try {
      const next = await settingsApi.patch(bookId, { styleGuide: value });
      queryClient.setQueryData(settingsKey(bookId), next);
      void queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
      failed.current = false;
      setError(null);
    } catch (e) {
      failed.current = true;
      setError(e instanceof Error ? e.message : 'The style guide could not be saved.');
      throw e;
    }
    return { version: 0 };
  };

  const settleRef = useRef<() => Promise<boolean>>(async () => true);
  const { controller, store, sync, pending } = useAutosaveController<string>(
    bookId,
    'settings',
    'style-guide',
    {
      value: saved,
      version: 0,
      save: (value) => {
        const work = send(value);
        inflight.current = work;
        return work;
      },
    },
    () => settleRef.current(),
  );
  // Text left unsent the last time the editor was open (its controller is still trying, or gave up and kept a draft).
  const [text, setText] = useState(pending?.value ?? saved);

  useEffect(() => {
    sync(text, 0);
  }, [sync, text]);

  // Waits for a save in flight (whose failure must not be missed), then saves what is left; twice, because text typed
  // while a request was out is saved by the second round.
  const settle = async () => {
    await inflight.current?.catch(ignore);
    failed.current = false;
    for (let round = 0; round < 2; round++) {
      await controller.flush();
      await inflight.current?.catch(ignore);
    }
    return !failed.current || store.getState() === 'offline';
  };

  settleRef.current = settle;

  return {
    text,
    setText,
    error,
    /** This editor's own save state (the book bar's dot shows the worst across the book). */
    store,
    /** Saves now; true when nothing is left that could not be saved (an offline edit counts: it is queued). */
    flush: settle,
  };
}
