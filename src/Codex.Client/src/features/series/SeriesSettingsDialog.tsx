import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { seriesApi } from '@codex/core/api/library';
import type { LibrarySeries } from '@codex/core/api/types';
import { showToast } from '@codex/core/editing/toastStore';
import { MarkdownSurface } from '../../prose';
import { libraryLink } from '@codex/core/shell/links';
import { OrderList } from './OrderList';
import { sameOrder } from '@codex/core/features/series/seriesOrder';
import { seriesTestIds } from './testIds';

interface SeriesSettingsDialogProps {
  series: LibrarySeries;
  /** Cancel, Esc, backdrop, × and a successful save all end here. */
  onClose: () => void;
  /** Closes this dialog and opens the delete-series confirmation. */
  onDelete: () => void;
}

const field = 'rounded border border-border bg-bg px-2 py-1 text-base text-text';
const button = 'h-8 rounded border border-border bg-transparent px-3 text-sm text-text';

// Modal: name, description, book order. A native <dialog> gives the dimmed backdrop, Esc and a Tab trap;
// the opener (SeriesPage) takes focus back in onClose.
export function SeriesSettingsDialog({ series, onClose, onDelete }: SeriesSettingsDialogProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [name, setName] = useState(series.name);
  const [description, setDescription] = useState(series.description ?? '');
  const [order, setOrder] = useState(() => series.books.map((b) => b.id));
  const [nameError, setNameError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const byId = new Map(series.books.map((b) => [b.id, b]));
  const books = order.flatMap((id) => byId.get(id) ?? []);
  const originalOrder = series.books.map((b) => b.id);

  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (name.trim() === '') {
      setNameError('A series needs a name.');
      return;
    }
    setNameError(null);
    setSaveError(null);
    setBusy(true);
    try {
      const textChanged =
        name.trim() !== series.name || description.trim() !== (series.description ?? '');
      if (textChanged) await seriesApi.patch(series.id, { name, description });
      if (!sameOrder(order, originalOrder)) await seriesApi.setOrder(series.id, { bookIds: order });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      onClose();
    } catch (e) {
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      setSaveError(e instanceof Error ? e.message : 'The series could not be saved.');
      setBusy(false);
    }
  };

  // Archiving is undoable (Restore series in the Library's Archived view), so it asks for no confirmation.
  const archive = async () => {
    setSaveError(null);
    setBusy(true);
    try {
      await seriesApi.archive(series.id);
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      showToast({ message: `${series.name} archived. Restore it from the Archived list.` });
      void navigate(libraryLink());
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'The series could not be archived.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={seriesTestIds.dialog}
      aria-labelledby="series-settings-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
      className="m-auto w-[min(500px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form onSubmit={(event) => void save(event)} noValidate>
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 id="series-settings-title" className="m-0 text-md font-semibold">
            Series settings
          </h3>
          <button
            type="button"
            data-testid={seriesTestIds.close}
            aria-label="Close"
            onClick={onClose}
            className="border-0 bg-transparent text-lg text-muted"
          >
            ×
          </button>
        </div>
        <div className="grid grid-cols-[88px_1fr] items-start gap-x-3 gap-y-3 p-4">
          <label htmlFor="series-name" className="pt-1 text-sm text-muted">
            Name
          </label>
          <div className="flex flex-col gap-1">
            <input
              id="series-name"
              data-testid={seriesTestIds.nameInput}
              value={name}
              autoComplete="off"
              aria-invalid={nameError !== null}
              onChange={(event) => setName(event.target.value)}
              className={field}
            />
            {nameError && (
              <span
                role="alert"
                data-testid={seriesTestIds.nameError}
                className="text-xs text-danger"
              >
                {nameError}
              </span>
            )}
          </div>
          <span className="pt-1 text-sm text-muted">Description</span>
          <div data-testid={seriesTestIds.descriptionInput} className={`${field} min-h-[60px]`}>
            <MarkdownSurface
              surface="series-settings"
              variant="prose"
              value={description}
              onChange={setDescription}
            />
          </div>
          <span className="pt-1 text-sm text-muted">Book order</span>
          <div className="flex flex-col gap-1">
            <OrderList books={books} onChange={setOrder} />
            <span className="text-xs text-muted">Book numbers follow this order.</span>
          </div>
        </div>
        <p className="m-0 px-4 pb-3 text-xs text-muted">
          Knowledge base categories on the series, like Characters and Places, are shared by every
          book in the series. Each book can add its own.
        </p>
        {saveError && (
          <p
            role="alert"
            data-testid={seriesTestIds.saveError}
            className="m-0 px-4 pb-3 text-xs text-danger"
          >
            {saveError}
          </p>
        )}
        <div className="flex justify-end gap-2 border-t border-border bg-bg px-4 py-3">
          <button
            type="button"
            data-testid={seriesTestIds.deleteButton}
            onClick={onDelete}
            className={`${button} mr-auto border-danger text-danger`}
          >
            Delete series…
          </button>
          <button
            type="button"
            data-testid={seriesTestIds.archiveButton}
            disabled={busy}
            onClick={() => void archive()}
            className={button}
          >
            Archive series
          </button>
          <button
            type="button"
            data-testid={seriesTestIds.cancel}
            onClick={onClose}
            className={button}
          >
            Cancel
          </button>
          <button
            type="submit"
            data-testid={seriesTestIds.save}
            disabled={busy}
            className="h-8 rounded border-0 bg-accent px-3 text-sm text-white"
          >
            Save
          </button>
        </div>
      </form>
    </dialog>
  );
}
