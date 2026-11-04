import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import { clearanceSummaryKey } from '@codex/core/api/references';
import {
  layoutSettingsKey,
  publicationKey,
  settingsApi,
  type PatchPublicationSettingsRequest,
  type PublicationField,
  type PublicationSettings,
} from '@codex/core/api/settings';
import { showToast } from '@codex/core/editing/toastStore';
import { useAutosave } from '../../editing/useAutosave';
import {
  fieldsOf,
  hasRequest,
  requestOf,
  type PublicationEdits,
  type PublicationFields,
} from '@codex/core/features/settings/publicationDraft';

export interface PublicationState {
  fields: PublicationFields;
  edit: (changes: PublicationEdits) => void;
  /** Saves the typed fields now (blur). */
  flush: () => void;
  /** Saves a choice at once (style, formats). */
  choose: (request: PatchPublicationSettingsRequest) => Promise<void>;
  /** "This book ×": back to the series value, with Undo. */
  reset: (field: PublicationField) => Promise<void>;
  error: string | null;
}

const requestFor = (
  settings: PublicationSettings,
  field: PublicationField,
): PatchPublicationSettingsRequest => {
  switch (field) {
    case 'style':
      return { style: settings.style.value };
    case 'quoteWordLimit':
      return { quoteWordLimit: settings.quoteWordLimit.value };
    case 'publisher':
      return { publisher: settings.publisher.value ?? '' };
    case 'plannedYear':
      return { plannedYear: settings.plannedYear.value ?? 0 };
    case 'formats':
      return { formats: settings.formats.value };
    case 'printRun':
      return { printRun: settings.printRun.value ?? 0 };
    case 'territory':
      return settings.territory.value === null ? {} : { territory: settings.territory.value };
  }
};

// The Citations & permissions values. Typed fields keep the reader's text in `edits` over the server's values and
// autosave like Plan (3 s, blur); a choice or a reset saves at once. A limit change moves the References badge
// and the print gate, so the book bar, the clearance summary and the layout gate refresh.
export function usePublication(settings: PublicationSettings): PublicationState {
  const queryClient = useQueryClient();
  const bookId = settings.bookId;
  const [edits, setEdits] = useState<PublicationEdits>({});
  const [error, setError] = useState<string | null>(null);

  const apply = async (request: PatchPublicationSettingsRequest) => {
    const next = await settingsApi.patchPublication(bookId, request);
    await settled(next);
  };

  const settled = async (next: PublicationSettings) => {
    queryClient.setQueryData(publicationKey(bookId), next);
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: clearanceSummaryKey(bookId) }),
      queryClient.invalidateQueries({ queryKey: ['rules-impact', bookId] }),
      queryClient.invalidateQueries({ queryKey: layoutSettingsKey(bookId) }),
    ]);
  };

  const { flush } = useAutosave<PublicationEdits>({
    value: edits,
    version: 0,
    save: async (value) => {
      const request = requestOf(value);
      if (!hasRequest(request)) return { version: 0 };
      try {
        await apply(request);
        setError(null);
        setEdits((current) => {
          const rest = { ...current };
          for (const key of Object.keys(value) as (keyof PublicationEdits)[]) {
            if (rest[key] === value[key]) delete rest[key];
          }
          return rest;
        });
      } catch (e) {
        setError(e instanceof Error ? e.message : 'The setting could not be saved.');
        throw e;
      }
      return { version: 0 };
    },
  });

  const choose = async (request: PatchPublicationSettingsRequest) => {
    try {
      await apply(request);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The setting could not be saved.');
    }
  };

  const reset = async (field: PublicationField) => {
    const before = requestFor(settings, field);
    try {
      const next = await settingsApi.resetPublication(bookId, field);
      await settled(next);
      setEdits({});
      setError(null);
      showToast({
        message: `Back to ${settings.seriesName ?? 'the series'} value.`,
        undo: () => apply(before),
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The setting could not be reset.');
    }
  };

  return {
    fields: { ...fieldsOf(settings), ...edits },
    edit: (changes) => setEdits((current) => ({ ...current, ...changes })),
    flush,
    choose,
    reset,
    error,
  };
}
