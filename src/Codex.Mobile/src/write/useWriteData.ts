import { hasRole, parseBookRole } from '@codex/core/api/bookRole';
import { blockApi, beatsKey } from '@codex/core/api/write';
import { useQuery } from '@tanstack/react-query';
import { useAdditions, useBookHeader, useKb, useManuscript, useOutline } from '~/book/queries';
import type { WriteData } from './buildLoad';

/** `GET /api/books/{id}/beats`. */
export function useBeats(bookId: string) {
  return useQuery({
    queryKey: beatsKey(bookId),
    queryFn: ({ signal }) => blockApi.beats(bookId, { signal }),
  });
}

export type WriteDataState =
  | { status: 'loading' }
  | { status: 'error'; error: unknown; retry: () => void }
  | { status: 'ready'; data: WriteData; readOnly: boolean };

/** Everything Write reads for a chapter, as one state: loading until all six reads are in, error on the first failure. */
export function useWriteData(bookId: string): WriteDataState {
  const header = useBookHeader(bookId);
  const outline = useOutline(bookId);
  const manuscript = useManuscript(bookId);
  const kb = useKb(bookId);
  const beats = useBeats(bookId);
  const additions = useAdditions(bookId);
  const all = [header, outline, manuscript, kb, beats, additions];

  // A background refetch that fails keeps the data it had: only a read that never succeeded is an error.
  const failed = all.find((query) => query.isError && query.data === undefined);
  if (failed) {
    return {
      status: 'error',
      error: failed.error,
      retry: () => {
        for (const query of all)
          if (query.isError && query.data === undefined) void query.refetch();
      },
    };
  }
  if (
    !header.data ||
    !outline.data ||
    !manuscript.data ||
    !kb.data ||
    !beats.data ||
    !additions.data
  ) {
    return { status: 'loading' };
  }
  return {
    status: 'ready',
    data: {
      outline: outline.data,
      manuscript: manuscript.data,
      kb: kb.data,
      beats: beats.data,
      additions: additions.data,
    },
    // Below the write role the page opens read-only (#80).
    readOnly: !hasRole(parseBookRole(header.data.role), 'write'),
  };
}
