import { useQuery } from '@tanstack/react-query';
import {
  getBookSettings,
  getLayoutSettings,
  getPublicationSettings,
  getPublishState,
  getRulesImpact,
  layoutSettingsKey,
  publicationKey,
  publishKey,
  rulesImpactKey,
  settingsKey,
} from '@codex/core/api/settings';

/** `GET …/publication-settings`: the Citations & permissions values, each with `inherited`. */
export function usePublicationSettings(bookId: string) {
  return useQuery({
    queryKey: publicationKey(bookId),
    queryFn: ({ signal }) => getPublicationSettings(bookId, signal),
  });
}

/** `GET …/settings/rules-impact?limit=`: the live strip for a limit that may not be saved yet. */
export function useRulesImpact(bookId: string, limit: number | null) {
  return useQuery({
    queryKey: rulesImpactKey(bookId, limit ?? 0),
    enabled: limit !== null && limit > 0,
    queryFn: ({ signal }) => getRulesImpact(bookId, limit ?? 0, signal),
  });
}

/** `GET …/settings/layout`: Layout & print, read-only. */
export function useLayoutSettings(bookId: string) {
  return useQuery({
    queryKey: layoutSettingsKey(bookId),
    queryFn: ({ signal }) => getLayoutSettings(bookId, signal),
  });
}

/** `GET /api/books/{id}/settings`: every section of the first half of Settings in one request. */
export function useBookSettings(bookId: string) {
  return useQuery({
    queryKey: settingsKey(bookId),
    queryFn: ({ signal }) => getBookSettings(bookId, signal),
  });
}

/** `GET …/publish`: the figures for the Published dialog and the snapshot list; polls while a snapshot builds. */
export function usePublishState(bookId: string) {
  return useQuery({
    queryKey: publishKey(bookId),
    queryFn: ({ signal }) => getPublishState(bookId, signal),
    refetchInterval: (query) =>
      query.state.data?.snapshots.some((s) => s.state === 'queued' || s.state === 'running')
        ? 2000
        : false,
  });
}
