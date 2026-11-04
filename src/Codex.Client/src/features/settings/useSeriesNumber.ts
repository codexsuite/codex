import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import {
  settingsApi,
  settingsKey,
  type BookSettings,
  type SeriesNumberChange,
} from '@codex/core/api/settings';
import { showToast } from '@codex/core/editing/toastStore';
import { numberLine, padded } from '@codex/core/features/settings/seriesNumbers';

export interface SeriesNumberState {
  /** The last change, so renumbered books can show `was 003`; cleared by an Undo. */
  change: SeriesNumberChange | null;
  error: string | null;
  set: (number: number) => Promise<void>;
}

// A number change inside the series applies at once and offers Undo in a toast (which puts the old number back).
export function useSeriesNumber(settings: BookSettings): SeriesNumberState {
  const queryClient = useQueryClient();
  const [change, setChange] = useState<SeriesNumberChange | null>(null);
  const [error, setError] = useState<string | null>(null);
  const bookId = settings.id;

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: settingsKey(bookId) });
    await queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
    await queryClient.invalidateQueries({ queryKey: ['library'] });
  };

  const apply = async (number: number): Promise<SeriesNumberChange> => {
    const result = await settingsApi.setNumber(bookId, { number });
    await refresh();
    return result;
  };

  const set = async (number: number) => {
    setError(null);
    try {
      const result = await apply(number);
      setChange(result);
      const fresh = queryClient.getQueryData<BookSettings>(settingsKey(bookId));
      const line = fresh
        ? numberLine(fresh.seriesOrder, result.number)
        : `Book ${padded(result.number)}`;
      showToast({
        message: `${settings.title} is now ${line}.`,
        undo: async () => {
          await apply(result.previousNumber);
          setChange(null);
        },
      });
    } catch (e) {
      await refresh();
      setError(e instanceof Error ? e.message : 'The number could not be changed.');
    }
  };

  return { change, error, set };
}
