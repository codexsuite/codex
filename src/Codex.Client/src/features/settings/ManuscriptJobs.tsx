import type { ExportJob, ManuscriptFormat } from '@codex/core/api/exports';
import { FORMATS } from './ExportManuscriptDialog';
import { settingsTestIds } from './testIds';

const STATUS: Record<string, string> = {
  queued: 'Queued',
  running: 'Running',
  failed: 'Failed',
  cancelled: 'Cancelled',
};

const formatLabel = (kind: string) => FORMATS.find((f) => f.code === kind)?.label ?? kind;

// The book's manuscript copies, newest first: where each is (Queued, Running, Done · file) with a download, what a
// finished one left out, and Retry for a failed one.
export function ManuscriptJobs({
  jobs,
  onRetry,
}: {
  jobs: ExportJob[];
  onRetry: (format: ManuscriptFormat) => void;
}) {
  if (jobs.length === 0) return null;
  return (
    <ul className="m-0 mt-2 flex list-none flex-col gap-1 p-0 text-sm">
      {jobs.slice(0, 5).map((job) => (
        <li
          key={job.id}
          data-testid={settingsTestIds.manuscriptJob}
          data-kind={job.kind}
          className="flex flex-wrap items-center gap-x-3 gap-y-1"
        >
          <span>{formatLabel(job.kind)}</span>
          <span data-testid={settingsTestIds.manuscriptJobStatus} className="text-text">
            {job.status === 'succeeded' ? `Done · ${job.filename ?? ''}` : STATUS[job.status]}
          </span>
          {job.fileUrl && (
            <a
              data-testid={settingsTestIds.manuscriptDownload}
              href={job.fileUrl}
              download={job.filename ?? undefined}
              className="text-accent"
            >
              Download
            </a>
          )}
          {job.status === 'failed' && (
            <>
              <span data-testid={settingsTestIds.manuscriptJobError} className="text-danger">
                {job.error}
              </span>
              <button
                type="button"
                data-testid={settingsTestIds.manuscriptRetry}
                onClick={() => onRetry(job.kind as ManuscriptFormat)}
                className="h-6 cursor-pointer rounded border border-border bg-surface px-2 text-sm text-text"
              >
                Retry
              </button>
            </>
          )}
          {job.notes && job.notes.length > 0 && (
            <span
              data-testid={settingsTestIds.manuscriptJobNotes}
              className="basis-full text-muted"
            >
              {job.notes.join(' · ')}
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}
