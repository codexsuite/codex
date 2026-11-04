import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { exportApi, exportKey, type ExportJob } from '@codex/core/api/exports';
import { ExportItem } from './ExportItem';
import type { ExportSession } from './useExports';
import { layoutTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface ExportsTabProps {
  session: ExportSession;
  onAgain: (job: ExportJob) => void;
}

// A running export (its step, progress and Cancel; it carries on after a reload, the server holds it) and past exports.
export function ExportsTab({ session, onAgain }: ExportsTabProps) {
  const [logFor, setLogFor] = useState<string | null>(null);
  const canWrite = useCanWrite();
  const log = useQuery({
    queryKey: exportKey(logFor),
    queryFn: ({ signal }) => exportApi.get(logFor ?? '', { signal }),
    enabled: logFor !== null,
  });
  const running = session.list?.exports.find((job) => job.id === session.list?.running);
  const past = (session.list?.exports ?? []).filter((job) => job.id !== running?.id);
  return (
    <div data-testid={layoutTestIds.exportsList} className="p-3">
      {session.error !== null && <p className="m-0 text-danger">{session.error}</p>}
      {running && (
        <div data-testid={layoutTestIds.exportRunning} className="mb-3 text-xs">
          <p className="m-0 font-semibold">Exporting the book</p>
          <p data-testid={layoutTestIds.exportSteps} className="m-0 text-muted">
            {running.step ?? 'Queued'} · {Math.round((running.progress ?? 0) * 100)}%
          </p>
          <progress className="w-full" max={1} value={running.progress ?? 0} />
          {canWrite && (
            <button
              type="button"
              data-testid={layoutTestIds.exportCancel}
              className="rounded-md border border-border px-2 py-0.5"
              onClick={() => session.cancel(running.id)}
            >
              Cancel
            </button>
          )}
        </div>
      )}
      {!running && past.length === 0 && session.list !== undefined && (
        <p className="m-0 text-muted">No exports yet.</p>
      )}
      <ul className="m-0 list-none p-0">
        {past.map((job) => (
          <ExportItem
            key={job.id}
            job={job}
            log={logFor === job.id ? (log.data?.log ?? null) : null}
            onLog={(open) => setLogFor(open ? job.id : null)}
            onAgain={onAgain}
          />
        ))}
      </ul>
    </div>
  );
}
