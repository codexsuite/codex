import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { proofApi, proofKey, proofListKey, type Proof } from '@codex/core/api/proofs';

export interface ProofSession {
  proof: Proof | undefined;
  /** The proof shown was found, not compiled for this request. */
  cached: boolean;
  /** The chapter has changed since the proof shown was made. */
  outOfDate: boolean;
  /** The request for a proof could not be sent (404, 400…). */
  requestError: string | null;
  rerender: () => void;
}

const POLL_MS = 600;

// The proof of a chapter shown by the Layout view's Proof mode. Opening it asks for a proof of the chapter as it is: the
// server answers with the cached one when nothing changed, or queues a compile the worker runs; an older proof of a
// chapter edited since is shown first, marked out of date, until Re-render. Remount (key) to start over for another chapter.
export function useProof(bookId: string, chapterId: string): ProofSession {
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [cached, setCached] = useState(false);
  const asked = useRef(false);

  const list = useQuery({
    queryKey: [...proofListKey(bookId), chapterId],
    queryFn: ({ signal }) => proofApi.list(bookId, chapterId, { signal }),
    retry: false,
  });
  const request = useMutation({
    mutationFn: (rerender: boolean) => proofApi.request(bookId, { chapterId, rerender }),
    onSuccess: (proof) => {
      setSelectedId(proof.id);
      setCached(proof.cached);
      void queryClient.invalidateQueries({ queryKey: [...proofListKey(bookId), chapterId] });
    },
  });

  const newest = list.data?.proofs[0];
  const older = newest !== undefined && newest.cacheKey !== list.data?.currentCacheKey;
  const shownId = selectedId ?? (older && newest.status === 'succeeded' ? newest.id : null);

  useEffect(() => {
    if (list.data === undefined || shownId !== null || asked.current) return;
    asked.current = true;
    request.mutate(false);
  }, [list.data, shownId, request]);

  const proof = useQuery({
    queryKey: proofKey(shownId),
    queryFn: ({ signal }) => proofApi.get(shownId ?? '', { signal }),
    enabled: shownId !== null,
    refetchInterval: (query) =>
      query.state.data?.status === 'queued' || query.state.data?.status === 'running'
        ? POLL_MS
        : false,
  });

  const shown = proof.data;
  return {
    proof: shown,
    cached: cached && shown?.id === selectedId,
    outOfDate:
      shown !== undefined &&
      list.data !== undefined &&
      shown.cacheKey !== list.data.currentCacheKey,
    requestError: list.error?.message ?? request.error?.message ?? null,
    rerender: () => request.mutate(true),
  };
}
