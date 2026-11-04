import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { lifecycleApi, type SeriesDeleteImpact } from '@codex/core/api/library';
import type { LibrarySeries } from '@codex/core/api/types';
import { showToast } from '@codex/core/editing/toastStore';
import { libraryLink } from '@codex/core/shell/links';
import { seriesTestIds } from './testIds';

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

function describe(impact: SeriesDeleteImpact): string {
  const books = `Its ${plural(impact.books, 'book becomes', 'books become')} standalone.`;
  const parts = [
    ...impact.categories.map((c) => `${c.name}, ${plural(c.entries, 'entry', 'entries')}`),
  ];
  const kb = parts.length > 0 ? `Knowledge base (${parts.join('; ')})` : '';
  const labels = impact.labels.length > 0 ? `labels (${impact.labels.join(', ')})` : '';
  const copied = [kb, labels].filter((p) => p !== '').join(' and ');
  return copied === '' ? books : `${books} The series' ${copied} are copied onto each book first.`;
}

// J17 step 9: says what the books keep, then detaches them (the server copies the series' Knowledge base and
// labels down) and deletes the series.
export function DeleteSeriesDialog({
  series,
  onClose,
}: {
  series: LibrarySeries;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const impact = useQuery({
    queryKey: ['library', 'series-delete-impact', series.id],
    queryFn: () => lifecycleApi.seriesImpact(series.id),
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await lifecycleApi.deleteSeries(series.id);
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      showToast({ message: `${series.name} deleted. Its books are standalone.` });
      void navigate(libraryLink());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The series could not be deleted.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={seriesTestIds.deleteDialog}
      aria-labelledby="delete-series-heading"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="delete-series-heading" className="m-0 text-md font-semibold">
          Delete {series.name}?
        </h3>
        <p data-testid={seriesTestIds.deleteImpact} className="m-0 text-sm">
          {impact.data ? describe(impact.data) : 'Checking what the books keep…'}
        </p>
        {error && (
          <p
            role="alert"
            data-testid={seriesTestIds.deleteError}
            className="m-0 text-xs text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={seriesTestIds.deleteCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={seriesTestIds.deleteConfirm}
            disabled={busy || impact.data === undefined}
            onClick={() => void confirm()}
            className="h-8 cursor-pointer rounded border-0 bg-danger px-3 text-base text-white disabled:opacity-50"
          >
            Delete series
          </button>
        </div>
      </div>
    </dialog>
  );
}
