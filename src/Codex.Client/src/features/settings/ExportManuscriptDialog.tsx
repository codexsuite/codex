import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
  manuscriptExportApi,
  manuscriptExportsKey,
  manuscriptInfoKey,
  type ManuscriptFormat,
} from '@codex/core/api/exports';
import { settingsTestIds } from './testIds';

export const FORMATS: { code: ManuscriptFormat; label: string }[] = [
  { code: 'docx', label: 'Word (.docx)' },
  { code: 'markdown', label: 'Markdown (.md)' },
  { code: 'epub', label: 'EPUB (.epub)' },
];

const NO_CHAPTERS = 'Nothing to export. This book has no chapters.';

interface ExportManuscriptDialogProps {
  bookId: string;
  /** The format chosen first (Retry opens it with the failed job's). */
  initialFormat: ManuscriptFormat;
  /** Formats with a job queued or running: one of each runs at a time. */
  busyFormats: ManuscriptFormat[];
  onClose: () => void;
}

// A Word, Markdown or EPUB copy of the manuscript for an editor or beta readers. Not for print, and not gated on References.
export function ExportManuscriptDialog({
  bookId,
  initialFormat,
  busyFormats,
  onClose,
}: ExportManuscriptDialogProps) {
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [format, setFormat] = useState<ManuscriptFormat>(initialFormat);
  const [scope, setScope] = useState<'book' | 'chapter'>('book');
  const [chapterId, setChapterId] = useState('');
  const [includeBeats, setIncludeBeats] = useState(false);
  const [includeSummaries, setIncludeSummaries] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const info = useQuery({
    queryKey: manuscriptInfoKey(bookId),
    queryFn: ({ signal }) => manuscriptExportApi.info(bookId, { signal }),
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const chapters = info.data?.chapters ?? [];
  const empty = info.data !== undefined && chapters.length === 0;
  const alreadyRunning = busyFormats.includes(format);
  const missingChapter = scope === 'chapter' && chapterId === '';
  const blocked = info.data === undefined || empty || alreadyRunning || missingChapter;

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await manuscriptExportApi.request(bookId, {
        format,
        scope,
        chapterId: scope === 'chapter' ? chapterId : undefined,
        includeBeats,
        includeSummaries,
      });
      await queryClient.invalidateQueries({ queryKey: manuscriptExportsKey(bookId) });
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The export could not be queued.');
      setBusy(false);
    }
  };

  const risk = info.data?.atRiskSources ?? 0;
  const field = 'flex flex-col gap-1 text-sm text-muted';
  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.manuscriptDialog}
      aria-labelledby="settings-manuscript-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-manuscript-title" className="m-0 text-md font-semibold">
          Export manuscript
        </h3>
        <div role="radiogroup" aria-label="Format" className={field}>
          Format
          <div className="flex gap-2">
            {FORMATS.map((f) => (
              <button
                key={f.code}
                type="button"
                role="radio"
                aria-checked={format === f.code}
                data-testid={settingsTestIds.manuscriptFormat(f.code)}
                onClick={() => setFormat(f.code)}
                className={`h-8 cursor-pointer rounded border px-3 text-base text-text ${
                  format === f.code ? 'border-accent bg-bg' : 'border-border bg-surface'
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>
        <label className={field}>
          Scope
          <select
            data-testid={settingsTestIds.manuscriptScope}
            value={scope}
            onChange={(e) => setScope(e.target.value === 'chapter' ? 'chapter' : 'book')}
            className="h-8 rounded border border-border bg-bg px-2 text-base text-text"
          >
            <option value="book">Full manuscript</option>
            <option value="chapter">One chapter</option>
          </select>
        </label>
        {scope === 'chapter' && (
          <label className={field}>
            Chapter
            <select
              data-testid={settingsTestIds.manuscriptChapter}
              value={chapterId}
              onChange={(e) => setChapterId(e.target.value)}
              className="h-8 rounded border border-border bg-bg px-2 text-base text-text"
            >
              <option value="">Pick a chapter…</option>
              {chapters.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.number === null ? c.title : `Chapter ${c.number} · ${c.title}`}
                </option>
              ))}
            </select>
          </label>
        )}
        <label className="flex items-center gap-2 text-base">
          <input
            type="checkbox"
            data-testid={settingsTestIds.manuscriptBeats}
            checked={includeBeats}
            onChange={(e) => setIncludeBeats(e.target.checked)}
          />
          Include beats
        </label>
        <label className="flex items-center gap-2 text-base">
          <input
            type="checkbox"
            data-testid={settingsTestIds.manuscriptSummaries}
            checked={includeSummaries}
            onChange={(e) => setIncludeSummaries(e.target.checked)}
          />
          Include summaries
        </label>
        {risk > 0 && (
          <p data-testid={settingsTestIds.manuscriptRisk} className="m-0 text-xs text-warn">
            {risk} {risk === 1 ? 'source is' : 'sources are'} at risk. This export is not gated.
          </p>
        )}
        {empty && (
          <p
            role="alert"
            data-testid={settingsTestIds.manuscriptError}
            className="m-0 text-sm text-danger"
          >
            {NO_CHAPTERS}
          </p>
        )}
        {alreadyRunning && (
          <p data-testid={settingsTestIds.manuscriptBusy} className="m-0 text-sm text-muted">
            An export is already running
          </p>
        )}
        {error && (
          <p
            role="alert"
            data-testid={settingsTestIds.manuscriptError}
            className="m-0 text-sm text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.manuscriptCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.manuscriptSubmit}
            disabled={busy || blocked}
            onClick={() => void submit()}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-base text-white disabled:opacity-50"
          >
            Export
          </button>
        </div>
      </div>
    </dialog>
  );
}
