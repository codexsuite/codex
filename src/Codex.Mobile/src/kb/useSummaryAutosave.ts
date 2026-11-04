import type { SaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { useEffect, useRef, useState } from 'react';
import { useAutosaveController } from '~/book/useAutosaveController';

/**
 * The summary text of an entry, saved 3 s after the first edit and on blur or when the screen closes, through the core's
 * controller on its own source of the book's save status (the book bar's dot shows the worst). Leaving with an edit that
 * could not go (offline) keeps the controller until it has been sent. The entry has no version, so every save is the
 * last write. The server's summary is an input: when it changes to something this field did not send (a recording
 * appended a transcript) and there is no unsaved edit, the field adopts it and the next edit builds on it. While there
 * is an unsaved edit, typing wins. A 403 puts the saved text back.
 */
export function useSummaryAutosave(
  bookId: string,
  entryId: string,
  serverSummary: string,
  save: (summary: string) => Promise<unknown>,
): {
  summary: string;
  setSummary: (summary: string) => void;
  flush: () => Promise<void>;
  store: SaveStatusStore;
} {
  // What the field and the server last agreed on: the text it loaded, adopted or sent.
  const baseline = useRef(serverSummary);
  const server = useRef(serverSummary);
  server.current = serverSummary;
  const versionRef = useRef(0);
  // The save in flight, so leaving or recording can wait for it (`controller.flush` returns at once while one is out).
  const inflight = useRef<Promise<unknown> | null>(null);

  const send = async (value: string) => {
    const before = baseline.current;
    baseline.current = value;
    try {
      await save(value);
    } catch (error) {
      baseline.current = before;
      throw error;
    }
    return { version: versionRef.current };
  };

  // Waits for a save in flight, sends what is left, and waits for that too. An edit that could not be sent (offline)
  // stays queued in the controller and goes when the phone is back online.
  const settle = async () => {
    for (let round = 0; round < 2; round++) {
      await inflight.current?.catch(() => undefined);
      await controller.flush();
    }
    await inflight.current?.catch(() => undefined);
  };
  const settleRef = useRef(settle);
  settleRef.current = settle;

  const { controller, sync, pending, store } = useAutosaveController<string>(
    bookId,
    'kb',
    entryId,
    {
      value: serverSummary,
      version: 0,
      save: (value) => {
        const work = send(value);
        inflight.current = work;
        return work;
      },
      // Refused as forbidden (#80): the edit is dropped; put the saved text back.
      onForbidden: () => {
        baseline.current = server.current;
        setSummary(server.current);
      },
    },
    () => settleRef.current(),
  );
  // Text left unsent the last time this entry was open (its controller is still trying, or gave up and kept a draft).
  const [summary, setSummary] = useState(pending?.value ?? serverSummary);
  // Bumped when the field adopts the server's text, so the controller takes it as its new baseline.
  const [version, setVersion] = useState(pending?.version ?? 0);
  versionRef.current = version;
  const typed = useRef(summary);
  typed.current = summary;

  useEffect(() => {
    if (serverSummary === baseline.current || typed.current !== baseline.current) return;
    baseline.current = serverSummary;
    setSummary(serverSummary);
    setVersion((v) => v + 1);
  }, [serverSummary]);
  useEffect(() => {
    sync(summary, version);
  }, [sync, summary, version]);
  return { summary, setSummary, flush: settle, store };
}
