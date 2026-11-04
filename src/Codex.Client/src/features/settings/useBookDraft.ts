import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import { settingsApi, settingsKey, type BookSettings } from '@codex/core/api/settings';
import { useAutosave } from '../../editing/useAutosave';
import {
  changesOf,
  draftOf,
  hasChanges,
  type BookDraft,
} from '@codex/core/features/settings/bookDraft';

export interface BookDraftState {
  draft: BookDraft;
  set: (changes: Partial<BookDraft>) => void;
  /** Sets and saves straight away (a choice). */
  choose: (changes: Partial<BookDraft>) => void;
  /** Saves now (blur). */
  flush: () => void;
}

// The Book and Writing fields, autosaved like Plan: 3 s after the first edit or straight away on blur or a choice.
// Only the fields that changed go in the PATCH. The server's answer refreshes the cache and the book bar.
export function useBookDraft(settings: BookSettings): BookDraftState {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState(() => draftOf(settings));
  const saved = useRef(draft);
  const bookId = settings.id;

  const { flush } = useAutosave<BookDraft>({
    value: draft,
    version: 0,
    save: async (value) => {
      const changes = changesOf(value, saved.current);
      if (!hasChanges(changes)) return { version: 0 };
      const next = await settingsApi.patch(bookId, changes);
      saved.current = { ...saved.current, ...draftOf(next) };
      queryClient.setQueryData(settingsKey(bookId), next);
      await queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      return { version: 0 };
    },
  });

  // A choice saves after the render that carries it, when the autosave has seen the new value.
  const [chosen, setChosen] = useState(0);
  const flushChosen = useEffectEvent(flush);
  useEffect(() => {
    if (chosen > 0) flushChosen();
  }, [chosen]);

  const set = (changes: Partial<BookDraft>) => setDraft((current) => ({ ...current, ...changes }));
  return {
    draft,
    set,
    choose: (changes) => {
      set(changes);
      setChosen((n) => n + 1);
    },
    flush,
  };
}
