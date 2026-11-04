import { useQueryClient } from '@tanstack/react-query';
import { bookKey } from '@codex/core/api/books';
import { clearanceSummaryKey } from '@codex/core/api/references';
import {
  layoutSettingsKey,
  publicationKey,
  settingsApi,
  settingsKey,
  type BookSettings,
  type PatchBookSettingsRequest,
  type PatchPublicationSettingsRequest,
  type PublicationField,
  type PublicationSettings,
} from '@codex/core/api/settings';

/**
 * The writes of the Settings tab, each with the cache refreshes the desktop does after it (the book bar, the Library,
 * and for a publication value the References badge and the layout gate). Every function rejects with the server's
 * `ApiRequestError`, so a sheet can show its message.
 */
export function useSettingsMutations(bookId: string) {
  const queryClient = useQueryClient();

  const bookChanged = (next: BookSettings) => {
    queryClient.setQueryData(settingsKey(bookId), next);
    void queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
    void queryClient.invalidateQueries({ queryKey: ['library'] });
  };

  const publicationChanged = (next: PublicationSettings) => {
    queryClient.setQueryData(publicationKey(bookId), next);
    void queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
    void queryClient.invalidateQueries({ queryKey: clearanceSummaryKey(bookId) });
    void queryClient.invalidateQueries({ queryKey: ['rules-impact', bookId] });
    void queryClient.invalidateQueries({ queryKey: layoutSettingsKey(bookId) });
  };

  return {
    saveBook: async (changes: PatchBookSettingsRequest) =>
      bookChanged(await settingsApi.patch(bookId, changes)),
    setSeriesNumber: async (number: number) => {
      await settingsApi.setNumber(bookId, { number });
      void queryClient.invalidateQueries({ queryKey: settingsKey(bookId) });
      void queryClient.invalidateQueries({ queryKey: bookKey(bookId) });
      void queryClient.invalidateQueries({ queryKey: ['library'] });
    },
    savePublication: async (request: PatchPublicationSettingsRequest) =>
      publicationChanged(await settingsApi.patchPublication(bookId, request)),
    resetPublication: async (field: PublicationField) =>
      publicationChanged(await settingsApi.resetPublication(bookId, field)),
    archive: async () => bookChanged(await settingsApi.archive(bookId)),
    restore: async () => bookChanged(await settingsApi.restore(bookId)),
  };
}
