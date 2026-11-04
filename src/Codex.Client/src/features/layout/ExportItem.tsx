import { useState } from 'react';
import type { ExportJob } from '@codex/core/api/exports';
import { layoutTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

const BUTTON = 'rounded-md border border-border px-2 py-0.5 text-xs';

const BLEEDS: Record<string, string> = {
  none: 'no bleed',
  '3mm': '3 mm bleed',
  '0.125in': '0.125 in bleed',
};

function size(bytes: number | null): string {
  if (bytes === null) return '';
  return bytes >= 1_048_576
    ? `${(bytes / 1_048_576).toFixed(1)} MB`
    : `${Math.ceil(bytes / 1024)} KB`;
}

function settings(job: ExportJob): string {
  if (job.kind === 'source') return 'Source .zip: Markdown, Knowledge base, References';
  return [
    `${job.template} v${job.templateVersion}`,
    job.trim,
    BLEEDS[job.bleed ?? 'none'],
    job.cropMarks ? 'crop marks' : null,
    job.quality === 'screen' ? '150 dpi' : '300 dpi',
    job.draft ? 'DRAFT' : null,
  ]
    .filter(Boolean)
    .join(' · ');
}

interface ExportItemProps {
  job: ExportJob;
  /** The full job (with its log), fetched when the log is opened. */
  log: string | null;
  onLog: (open: boolean) => void;
  onAgain: (job: ExportJob) => void;
}

// A past export: when, its settings and template version, Download, Export again with these settings, and for a failed one the log.
export function ExportItem({ job, log, onLog, onAgain }: ExportItemProps) {
  const [logOpen, setLogOpen] = useState(false);
  // Exporting needs write (#80).
  const canWrite = useCanWrite();
  const done = job.status === 'succeeded';
  return (
    <li
      data-testid={
        job.kind === 'source' ? layoutTestIds.exportSourceItem : layoutTestIds.exportItem
      }
      data-status={job.status}
      data-kind={job.kind}
      className="mb-3 border-b border-border pb-2 text-xs"
    >
      <div className="flex items-center gap-2">
        <span className="font-semibold">{job.kind === 'source' ? 'Source' : 'Export'}</span>
        <span className="text-muted">{job.createdLabel}</span>
        {done && (
          <span data-testid={layoutTestIds.exportDone} className="text-ok">
            Done{job.pageCount !== null ? ` · ${job.pageCount} pages` : ''}
            {job.bytes !== null ? ` · ${size(job.bytes)}` : ''}
          </span>
        )}
        {job.status === 'failed' && <span className="text-danger">Failed</span>}
        {job.status === 'cancelled' && <span className="text-muted">Cancelled</span>}
        {(job.status === 'queued' || job.status === 'running') && (
          <span className="text-muted">{job.step ?? job.status}</span>
        )}
      </div>
      <div className="text-muted">{settings(job)}</div>
      {job.error !== null && job.status === 'failed' && (
        <div className="text-danger">{job.error}</div>
      )}
      {job.overfull.length > 0 && (
        <div className="text-warn">{job.overfull.length} overfull lines in the log</div>
      )}
      <div className="mt-1 flex gap-2">
        {job.fileUrl !== null && (
          <a
            data-testid={layoutTestIds.exportDownload}
            className={`${BUTTON} text-accent`}
            href={job.fileUrl}
            download
          >
            Download
          </a>
        )}
        {canWrite &&
          job.kind === 'print' &&
          job.status !== 'queued' &&
          job.status !== 'running' && (
            <button
              type="button"
              data-testid={layoutTestIds.exportAgain}
              className={BUTTON}
              onClick={() => onAgain(job)}
            >
              Export again with these settings
            </button>
          )}
        {job.kind === 'print' && job.status !== 'queued' && (
          <button
            type="button"
            data-testid={layoutTestIds.exportLogToggle}
            aria-pressed={logOpen}
            className={BUTTON}
            onClick={() => {
              setLogOpen(!logOpen);
              onLog(!logOpen);
            }}
          >
            Log
          </button>
        )}
      </div>
      {logOpen && (
        <pre
          data-testid={layoutTestIds.exportLog}
          className="mt-1 max-h-48 overflow-auto bg-surface-2 p-2 text-[10px]"
        >
          {log ?? 'Loading the log…'}
        </pre>
      )}
    </li>
  );
}
