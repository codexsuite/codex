import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
  exportApi,
  preflightKey,
  type ExportBleed,
  type ExportQuality,
  type ExportRequest,
} from '@codex/core/api/exports';
import type { LayoutState } from '@codex/core/api/layout';
import { PreflightList } from './PreflightList';
import type { SceneInfo } from './sceneLabels';
import { layoutTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

const BLEEDS: { value: ExportBleed; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: '3mm', label: '3 mm' },
  { value: '0.125in', label: '0.125 in' },
];
const QUALITIES: { value: ExportQuality; label: string }[] = [
  { value: 'print', label: 'Print 300 dpi' },
  { value: 'screen', label: 'Screen 150 dpi' },
];
const SEGMENT = 'border border-border px-2 py-1 text-xs first:rounded-l-md last:rounded-r-md';

interface ExportDialogProps {
  bookId: string;
  layout: LayoutState;
  /** Settings to start from: the defaults, or those of the export being repeated. */
  initial: ExportRequest;
  sceneInfo: (sceneId: string) => SceneInfo | undefined;
  onClose: () => void;
  /** Resolves with the server's refusal, or null once queued. */
  onQueue: (request: ExportRequest) => Promise<string | null>;
}

// Export for printer: the pinned template and trim, bleed, crop marks (only with a bleed), image quality and the format, ending in
// the preflight of the whole book. Blocking issues disable Queue export unless the export is a draft with DRAFT on every page.
export function ExportDialog({
  bookId,
  layout,
  initial,
  sceneInfo,
  onClose,
  onQueue,
}: ExportDialogProps) {
  useReturnFocus();
  const dialogRef = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);
  const [bleed, setBleed] = useState(initial.bleed);
  const [cropMarks, setCropMarks] = useState(initial.cropMarks);
  const [quality, setQuality] = useState(initial.quality);
  const [draft, setDraft] = useState(initial.draft);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const template = layout.templates.find((t) => t.slug === layout.template);
  const request: ExportRequest = {
    template: layout.template,
    trim: layout.trim,
    bleed,
    cropMarks: cropMarks && bleed !== 'none',
    quality,
    format: 'pdf',
    draft,
  };
  const preflight = useQuery({
    queryKey: [...preflightKey(bookId), request.template, request.trim, bleed, quality],
    queryFn: ({ signal }) => exportApi.preflight(bookId, { ...request, draft: false }, { signal }),
    retry: false,
  });
  const blocked = preflight.data?.verdict === 'blocked';
  const canQueue = preflight.data !== undefined && !busy && (!blocked || draft);

  const queue = async () => {
    setBusy(true);
    setError(null);
    const refused = await onQueue(request);
    setBusy(false);
    if (refused === null) onClose();
    else setError(refused);
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={layoutTestIds.exportDialog}
      aria-labelledby="layout-export-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(520px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex max-h-[85vh] flex-col gap-3 overflow-auto p-4 text-sm">
        <h3 id="layout-export-title" className="m-0 text-md font-semibold">
          Export for printer
        </h3>
        <p className="m-0 text-muted">
          {template?.name ?? layout.template} v{template?.version} · trim {layout.trim} · PDF
        </p>
        <fieldset className="m-0 border-0 p-0">
          <legend className="mb-1 text-xs text-muted">Bleed</legend>
          <div className="flex" role="group" aria-label="Bleed">
            {BLEEDS.map((b) => (
              <button
                key={b.value}
                type="button"
                aria-pressed={bleed === b.value}
                data-testid={layoutTestIds.exportBleed(b.value)}
                className={`${SEGMENT} ${bleed === b.value ? 'bg-accent text-accent-fg' : ''}`}
                onClick={() => setBleed(b.value)}
              >
                {b.label}
              </button>
            ))}
          </div>
        </fieldset>
        <label className="flex items-center gap-2 text-xs">
          <input
            type="checkbox"
            data-testid={layoutTestIds.exportCrop}
            checked={cropMarks && bleed !== 'none'}
            disabled={bleed === 'none'}
            onChange={(e) => setCropMarks(e.target.checked)}
          />
          Crop marks (need a bleed)
        </label>
        <fieldset className="m-0 border-0 p-0">
          <legend className="mb-1 text-xs text-muted">Image quality</legend>
          <div className="flex" role="group" aria-label="Image quality">
            {QUALITIES.map((q) => (
              <button
                key={q.value}
                type="button"
                aria-pressed={quality === q.value}
                data-testid={layoutTestIds.exportQuality(q.value)}
                className={`${SEGMENT} ${quality === q.value ? 'bg-accent text-accent-fg' : ''}`}
                onClick={() => setQuality(q.value)}
              >
                {q.label}
              </button>
            ))}
          </div>
        </fieldset>
        <section data-testid={layoutTestIds.exportPreflight}>
          {preflight.isPending && <p className="m-0 text-muted">Checking the book…</p>}
          {preflight.isError && <p className="m-0 text-danger">{preflight.error.message}</p>}
          {preflight.data && (
            <PreflightList bookId={bookId} preflight={preflight.data} sceneInfo={sceneInfo} />
          )}
        </section>
        {blocked && (
          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              data-testid={layoutTestIds.exportDraft}
              checked={draft}
              onChange={(e) => setDraft(e.target.checked)}
            />
            Export a draft with DRAFT across every page
          </label>
        )}
        {preflight.data && (
          <p data-testid={layoutTestIds.exportSize} className="m-0 text-xs text-muted">
            About {preflight.data.estimatedMegabytes} MB
          </p>
        )}
        {error !== null && (
          <p data-testid={layoutTestIds.exportError} role="alert" className="m-0 text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={layoutTestIds.exportCancelDialog}
            className="h-8 rounded border border-border bg-surface px-3 text-base"
            onClick={onClose}
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={layoutTestIds.exportQueue}
            disabled={!canQueue}
            className="h-8 rounded border-0 bg-accent px-3 text-base text-accent-fg disabled:opacity-50"
            onClick={() => void queue()}
          >
            Queue export
          </button>
        </div>
      </div>
    </dialog>
  );
}
