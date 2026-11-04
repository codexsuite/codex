import { useQuery } from '@tanstack/react-query';
import { DEFAULT_EXPORT, exportApi, preflightKey } from '@codex/core/api/exports';
import { PreflightList } from './PreflightList';
import type { SceneInfo } from './sceneLabels';

interface PreflightTabProps {
  bookId: string;
  /** Changes when the template, trim or an option does, so the findings follow it. */
  fingerprint: string;
  sceneInfo: (sceneId: string) => SceneInfo | undefined;
}

// Preflight of the whole book for the book's template and trim at print quality, the same findings Export for printer shows.
export function PreflightTab({ bookId, fingerprint, sceneInfo }: PreflightTabProps) {
  const preflight = useQuery({
    queryKey: [...preflightKey(bookId), DEFAULT_EXPORT, fingerprint],
    queryFn: ({ signal }) => exportApi.preflight(bookId, DEFAULT_EXPORT, { signal }),
    retry: false,
  });
  return (
    <div className="p-3">
      {preflight.isPending && <p className="m-0 text-muted">Checking the book…</p>}
      {preflight.isError && <p className="m-0 text-danger">{preflight.error.message}</p>}
      {preflight.data && (
        <PreflightList bookId={bookId} preflight={preflight.data} sceneInfo={sceneInfo} />
      )}
    </div>
  );
}
