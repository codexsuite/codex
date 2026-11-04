import { useQuery } from '@tanstack/react-query';
import {
  additionApi,
  additionsKey,
  beatsKey,
  blockApi,
  draftApi,
  draftsKey,
  manuscriptKey,
  writeApi,
} from '@codex/core/api/write';

export function useManuscript(bookId: string) {
  return useQuery({
    queryKey: manuscriptKey(bookId),
    queryFn: ({ signal }) => writeApi.manuscript(bookId, { signal }),
  });
}

export function useBookBeats(bookId: string) {
  return useQuery({
    queryKey: beatsKey(bookId),
    queryFn: ({ signal }) => blockApi.beats(bookId, { signal }),
  });
}

export function useBookAdditions(bookId: string) {
  return useQuery({
    queryKey: additionsKey(bookId),
    queryFn: ({ signal }) => additionApi.list(bookId, { signal }),
  });
}

export function useBookDrafts(bookId: string) {
  return useQuery({
    queryKey: draftsKey(bookId),
    queryFn: ({ signal }) => draftApi.drafts(bookId, { signal }),
  });
}
