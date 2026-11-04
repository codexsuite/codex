import { useQuery } from '@tanstack/react-query';
import {
  getLibrary,
  getNextSeriesNumber,
  libraryKey,
  nextNumberKey,
  type LibraryParams,
} from '@codex/core/api/library';

/** `GET /api/library`. An unknown cover palette rejects the whole response (shown as the load error). */
export function useLibrary(params: LibraryParams) {
  return useQuery({
    queryKey: libraryKey(params),
    queryFn: ({ signal }) => getLibrary(params, signal),
  });
}

/** `GET /api/series/{id}/next-number`: one above the highest number, archived books counted. */
export function useNextSeriesNumber(seriesId: string | null) {
  return useQuery({
    queryKey: nextNumberKey(seriesId ?? ''),
    enabled: seriesId !== null,
    queryFn: ({ signal }) => getNextSeriesNumber(seriesId ?? '', signal),
  });
}
