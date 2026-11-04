import { useQuery } from '@tanstack/react-query';
import {
  exportApi,
  exportsKey,
  manuscriptExportApi,
  manuscriptExportsKey,
  type ExportJob,
} from '@codex/core/api/exports';

const POLL_MS = 600;

const live = (jobs: ExportJob[] | undefined) =>
  jobs?.some((job) => job.status === 'queued' || job.status === 'running') ?? false;

export interface DocumentExports {
  /** Word, Markdown and EPUB copies, newest first. */
  manuscripts: ExportJob[];
  /** The newest source (backup) export. */
  source: ExportJob | undefined;
}

// What Archive & export shows about the book's background exports. Read from the server, so a running one survives a
// reload, and polled while any is queued or running. The print list is the one the Layout screen reads.
export function useDocumentExports(bookId: string): DocumentExports {
  const manuscripts = useQuery({
    queryKey: manuscriptExportsKey(bookId),
    queryFn: ({ signal }) => manuscriptExportApi.list(bookId, { signal }),
    refetchInterval: (query) => (live(query.state.data?.exports) ? POLL_MS : false),
  });
  const all = useQuery({
    queryKey: exportsKey(bookId),
    queryFn: ({ signal }) => exportApi.list(bookId, { signal }),
    refetchInterval: (query) =>
      query.state.data?.exports.some((job) => job.kind === 'source' && live([job]))
        ? POLL_MS
        : false,
  });
  return {
    manuscripts: manuscripts.data?.exports ?? [],
    source: all.data?.exports.find((job) => job.kind === 'source'),
  };
}
