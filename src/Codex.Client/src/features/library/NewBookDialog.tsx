import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { booksApi, defaultLibraryParams } from '@codex/core/api/library';
import { useLibrary, useNextSeriesNumber } from '../../api/libraryQueries';
import { planLink } from '@codex/core/shell/links';
import { libraryTestIds } from './testIds';
import { seriesShare } from '@codex/core/features/library/sharedBooks';
import { useReturnFocus } from './useReturnFocus';

interface NewBookDialogProps {
  /** Pre-selected series (from the Series screen). */
  seriesId?: string;
  /** Cancel, Esc, backdrop and × end here; a created book navigates to its Plan instead. */
  onClose: () => void;
}

const field = 'rounded border border-border bg-bg px-2 py-1 text-base text-text';
const button = 'h-8 rounded border border-border bg-transparent px-3 text-sm text-text';

function positiveInteger(text: string): number | null {
  const digits = text.replace(/[,\s]/g, '');
  return /^\d+$/.test(digits) && Number(digits) > 0 ? Number(digits) : null;
}

// J3: Title, Series, Number (only in a series, pre-filled with the next one) and Target words. A native <dialog>
// gives the dimmed backdrop, Esc and a Tab trap. Create opens the new book's Plan.
export function NewBookDialog({ seriesId, onClose }: NewBookDialogProps) {
  useReturnFocus();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleRef = useRef<HTMLInputElement>(null);
  const library = useLibrary(defaultLibraryParams);
  const [title, setTitle] = useState('');
  const [chosenSeries, setChosenSeries] = useState(seriesId ?? '');
  // null = untouched: the field shows the server's next number.
  const [numberText, setNumberText] = useState<string | null>(null);
  const [targetText, setTargetText] = useState('80,000');
  const [errors, setErrors] = useState<{ title?: string; number?: string; target?: string }>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const next = useNextSeriesNumber(chosenSeries === '' ? null : chosenSeries);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    titleRef.current?.focus();
  }, []);

  const shownNumber = numberText ?? (next.data ? String(next.data.nextNumber) : '');

  const create = async (event: FormEvent) => {
    event.preventDefault();
    const found: typeof errors = {};
    if (title.trim() === '') found.title = 'Give the book a title.';
    const number = positiveInteger(shownNumber);
    if (chosenSeries !== '' && number === null) {
      found.number = 'Give the book a number in the series.';
    }
    const target = targetText.trim() === '' ? undefined : positiveInteger(targetText);
    if (target === null) found.target = 'Target words must be a whole number above 0.';
    setErrors(found);
    setSaveError(null);
    if (Object.keys(found).length > 0) return;

    setBusy(true);
    try {
      const created = await booksApi.create({
        title: title.trim(),
        seriesId: chosenSeries === '' ? undefined : chosenSeries,
        seriesNumber: chosenSeries === '' ? undefined : (number ?? undefined),
        targetWords: target ?? undefined,
      });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      void navigate(planLink(created.id));
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'The book could not be created.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={libraryTestIds.newBookDialog}
      aria-labelledby="new-book-title-heading"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
      className="m-auto w-[min(460px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form onSubmit={(event) => void create(event)} noValidate>
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 id="new-book-title-heading" className="m-0 text-md font-semibold">
            New book
          </h3>
          <button
            type="button"
            data-testid={libraryTestIds.newBookClose}
            aria-label="Close"
            onClick={onClose}
            className="border-0 bg-transparent text-lg text-muted"
          >
            ×
          </button>
        </div>
        <div className="grid grid-cols-[96px_1fr] items-start gap-x-3 gap-y-3 p-4">
          <label htmlFor="new-book-title" className="pt-1 text-sm text-muted">
            Title
          </label>
          <div className="flex flex-col gap-1">
            <input
              id="new-book-title"
              data-testid={libraryTestIds.newBookTitle}
              ref={titleRef}
              placeholder="Untitled"
              autoComplete="off"
              value={title}
              aria-invalid={errors.title !== undefined}
              onChange={(event) => setTitle(event.target.value)}
              className={field}
            />
            {errors.title && (
              <span
                role="alert"
                data-testid={libraryTestIds.newBookTitleError}
                className="text-xs text-danger"
              >
                {errors.title}
              </span>
            )}
          </div>
          <label htmlFor="new-book-series" className="pt-1 text-sm text-muted">
            Series
          </label>
          <select
            id="new-book-series"
            data-testid={libraryTestIds.newBookSeries}
            value={chosenSeries}
            onChange={(event) => {
              setChosenSeries(event.target.value);
              setNumberText(null);
            }}
            className={field}
          >
            <option value="">None (standalone)</option>
            {/* Only your own series: a new book joins one of your series (#80). */}
            {library.data?.series
              .filter((s) => seriesShare(s) === null)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
          </select>
          {chosenSeries !== '' && (
            <>
              <label htmlFor="new-book-number" className="pt-1 text-sm text-muted">
                Number
              </label>
              <div className="flex flex-col gap-1">
                <input
                  id="new-book-number"
                  data-testid={libraryTestIds.newBookNumber}
                  inputMode="numeric"
                  autoComplete="off"
                  value={shownNumber}
                  aria-invalid={errors.number !== undefined}
                  onChange={(event) => setNumberText(event.target.value)}
                  className={`${field} w-20`}
                />
                {errors.number && (
                  <span
                    role="alert"
                    data-testid={libraryTestIds.newBookNumberError}
                    className="text-xs text-danger"
                  >
                    {errors.number}
                  </span>
                )}
              </div>
            </>
          )}
          <label htmlFor="new-book-target" className="pt-1 text-sm text-muted">
            Target words
          </label>
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <input
                id="new-book-target"
                data-testid={libraryTestIds.newBookTarget}
                inputMode="numeric"
                autoComplete="off"
                value={targetText}
                aria-invalid={errors.target !== undefined}
                onChange={(event) => setTargetText(event.target.value)}
                className={`${field} w-24`}
              />
              <span className="text-xs text-muted">You can change it later</span>
            </div>
            {errors.target && (
              <span
                role="alert"
                data-testid={libraryTestIds.newBookTargetError}
                className="text-xs text-danger"
              >
                {errors.target}
              </span>
            )}
          </div>
        </div>
        {saveError && (
          <p
            role="alert"
            data-testid={libraryTestIds.newBookError}
            className="m-0 px-4 pb-3 text-xs text-danger"
          >
            {saveError}
          </p>
        )}
        <div className="flex items-center gap-2 border-t border-border bg-bg px-4 py-3">
          <span className="flex-1 text-xs text-muted">Opens on Plan with one empty act</span>
          <button
            type="button"
            data-testid={libraryTestIds.newBookCancel}
            onClick={onClose}
            className={button}
          >
            Cancel
          </button>
          <button
            type="submit"
            data-testid={libraryTestIds.newBookCreate}
            disabled={busy}
            className="h-8 rounded border-0 bg-accent px-3 text-sm text-white"
          >
            Create book
          </button>
        </div>
      </form>
    </dialog>
  );
}
