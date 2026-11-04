import { AutosaveController, type AutosaveOptions } from '@codex/core/editing/autosaveController';
import type { SaveStatusStore } from '@codex/core/editing/saveStatusStore';
import { useCallback, useEffect, useRef, useState } from 'react';
import { forgetDraft, keepDraft, readDraft } from './drafts';
import { leave, liveAutosaves, type LiveAutosave } from './liveAutosaves';
import { bookSaveStatus } from './saveStatus';

export interface BookAutosave<T> {
  controller: AutosaveController<T>;
  /** This autosave's own state (the book bar's dot shows the worst of all of them). */
  store: SaveStatusStore;
  /** Reports the field's value to the controller (use this, not `controller.sync`, so a screen coming back can adopt it). */
  sync(value: T, version: number): void;
  /**
   * Text this field was left with that the server does not have: a controller still alive from the last time the field
   * was open, or a draft kept when its retries ran out (only while the server still has the text it was based on). The
   * field starts from it. Null when there is none.
   */
  pending: { value: T; version: number } | null;
}

interface Held<T> {
  live: LiveAutosave;
  pending: { value: T; version: number } | null;
}

/**
 * An `AutosaveController` for one autosaving field, reporting to its own source of the book's save status and living as
 * long as it has something to save. Sources and controllers are keyed `area:id` within the book: opening the field
 * again while its controller is alive adopts that controller (and its pending text), so there is never a second one
 * racing it. Leaving flushes (or runs `settle`, for a caller that first waits for a request in flight), then keeps the
 * controller only while it can still send the edit (see `leave` in `liveAutosaves.ts`) and retires it after that.
 * `options.value` is the server's current text: a kept draft is only restored while it is still what the draft started from.
 */
export function useAutosaveController<T>(
  bookId: string,
  area: string,
  id: string,
  options: Omit<AutosaveOptions<T>, 'store'>,
  settle?: () => Promise<unknown>,
): BookAutosave<T> {
  const key = `${area}:${id}`;
  // What the controller is configured with: saving moves the base of a later draft to what the server now has, and a
  // saved value is no longer a draft, even when the save lands after the screen has gone.
  const withBase = (live: LiveAutosave): Omit<AutosaveOptions<T>, 'store'> => ({
    ...options,
    save: async (value, version) => {
      const result = await options.save(value, version);
      live.base = value;
      if (Object.is(live.latest.value, value)) forgetDraft(bookId, key);
      return result;
    },
  });

  const acquire = (): Held<T> => {
    const existing = liveAutosaves.get(bookId, key);
    if (existing) {
      const unsaved = existing.store.getState() !== 'saved';
      return {
        live: existing,
        pending: unsaved ? (existing.latest as { value: T; version: number }) : null,
      };
    }
    const store = bookSaveStatus.forBook(bookId).store(key);
    const created: LiveAutosave = {
      controller: new AutosaveController<T>({ ...options, store }),
      store,
      latest: { value: options.value, version: options.version },
      base: options.value,
      claims: 0,
    };
    liveAutosaves.add(bookId, key, created);
    const draft = readDraft<T>(bookId, key, options.value);
    return {
      live: created,
      pending: draft === undefined ? null : { value: draft, version: options.version },
    };
  };

  const acquireRef = useRef(acquire);
  acquireRef.current = acquire;
  const [held, setHeld] = useState(acquire);
  const live = held.live;
  const controller = live.controller as AutosaveController<T>;
  controller.configure({ ...withBase(live), store: live.store });

  const settleRef = useRef(settle);
  settleRef.current = settle;
  useEffect(() => {
    // The controller adopted while rendering was retired by the screen that left before this one mounted: start afresh.
    if (liveAutosaves.get(bookId, key) !== live) {
      setHeld(acquireRef.current());
      return;
    }
    liveAutosaves.claim(live);
    return () => {
      leave(live, {
        settle: () => (settleRef.current ?? (() => controller.flush()))(),
        retire: (unsaved) => {
          if (unsaved) keepDraft(bookId, key, live.latest.value, live.base);
          else forgetDraft(bookId, key);
          controller.dispose();
          bookSaveStatus.forBook(bookId).release(key, live.store);
          liveAutosaves.remove(bookId, key, live);
        },
      });
    };
  }, [live, controller, bookId, key]);

  // A saved edit is no longer a draft.
  useEffect(() => {
    // Not on mount: a draft the field has just taken up stays until its save has gone through.
    const clear = () => {
      if (live.store.getState() === 'saved') forgetDraft(bookId, key);
    };
    return live.store.subscribe(clear);
  }, [live, bookId, key]);

  // The field reporting the server's own text (it took a newer copy, or loaded) moves the base a later draft is made on.
  const serverRef = useRef(options.value);
  serverRef.current = options.value;
  const sync = useCallback(
    (value: T, version: number) => {
      if (Object.is(value, serverRef.current)) live.base = value;
      live.latest = { value, version };
      controller.sync(value, version);
    },
    [live, controller],
  );

  return { controller, store: live.store, sync, pending: held.pending };
}
