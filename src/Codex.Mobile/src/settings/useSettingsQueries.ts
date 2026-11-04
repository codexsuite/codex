import { useQuery } from '@tanstack/react-query';
import {
  getLayoutSettings,
  getPublicationSettings,
  layoutSettingsKey,
  publicationKey,
} from '@codex/core/api/settings';

/** The Citations & permissions values, each with `inherited`. */
export function usePublicationSettings(bookId: string) {
  return useQuery({
    queryKey: publicationKey(bookId),
    queryFn: ({ signal }) => getPublicationSettings(bookId, signal),
  });
}

/** Layout & print, read-only on mobile. */
export function useLayoutSettings(bookId: string) {
  return useQuery({
    queryKey: layoutSettingsKey(bookId),
    queryFn: ({ signal }) => getLayoutSettings(bookId, signal),
  });
}
