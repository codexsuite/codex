import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  exportApi,
  exportsKey,
  type ExportList,
  type ExportRequest,
} from '@codex/core/api/exports';

const POLL_MS = 600;

export interface ExportSession {
  list: ExportList | undefined;
  error: string | null;
  cancel: (exportId: string) => void;
  /** Queues an export; resolves with the error message if the server refused it. */
  queue: (request: ExportRequest) => Promise<string | null>;
}

// The book's exports. Read from the server, so a running export is still there after a reload, and polled while any (a source
// export queued from Settings too) is queued or running.
export function useExports(bookId: string): ExportSession {
  const queryClient = useQueryClient();
  const list = useQuery({
    queryKey: exportsKey(bookId),
    queryFn: ({ signal }) => exportApi.list(bookId, { signal }),
    refetchInterval: (query) =>
      query.state.data?.exports.some((job) => job.status === 'queued' || job.status === 'running')
        ? POLL_MS
        : false,
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: exportsKey(bookId) });
  const cancel = useMutation({ mutationFn: exportApi.cancel, onSuccess: refresh });
  const queue = useMutation({
    mutationFn: (request: ExportRequest) => exportApi.request(bookId, request),
    onSuccess: refresh,
  });
  return {
    list: list.data,
    error: list.error?.message ?? cancel.error?.message ?? null,
    cancel: (exportId) => cancel.mutate(exportId),
    queue: async (request) => {
      try {
        await queue.mutateAsync(request);
        return null;
      } catch (error) {
        return error instanceof Error ? error.message : 'The export could not be queued.';
      }
    },
  };
}
