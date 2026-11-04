import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { defaultLibraryParams, lifecycleApi } from '@codex/core/api/library';
import { useLibrary } from '../../api/libraryQueries';
import { libraryTestIds } from './testIds';
import { useReturnFocus } from './useReturnFocus';

const field = 'rounded border border-border bg-bg px-2 py-1 text-base text-text';
const button = 'h-8 rounded border border-border bg-transparent px-3 text-sm text-text';

// J27 step 1: a name and the standalone books to gather, picked in order (the order sets Book 001, 002, ...).
// An empty series is allowed. A name already in use only gets a notice: titles are not unique.
export function NewSeriesDialog({ onClose }: { onClose: () => void }) {
  useReturnFocus();
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const library = useLibrary(defaultLibraryParams);
  const [name, setName] = useState('');
  const [picked, setPicked] = useState<string[]>([]);
  const [nameError, setNameError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
    nameRef.current?.focus();
  }, []);

  const standalone = library.data?.standalone ?? [];
  const titleOf = (id: string) => standalone.find((b) => b.id === id)?.title ?? id;
  const nameTaken =
    name.trim() !== '' &&
    library.data?.series.some((s) => s.name.toLowerCase() === name.trim().toLowerCase());
  const toggle = (id: string) =>
    setPicked((current) =>
      current.includes(id) ? current.filter((x) => x !== id) : [...current, id],
    );

  const create = async (event: FormEvent) => {
    event.preventDefault();
    if (name.trim() === '') {
      setNameError('A series needs a name.');
      return;
    }
    setNameError(null);
    setSaveError(null);
    setBusy(true);
    try {
      await lifecycleApi.createSeries({ name: name.trim(), bookIds: picked });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      onClose();
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : 'The series could not be created.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={libraryTestIds.newSeriesDialog}
      aria-labelledby="new-series-heading"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(500px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <form onSubmit={(event) => void create(event)} noValidate>
        <div className="border-b border-border px-4 py-3">
          <h3 id="new-series-heading" className="m-0 text-md font-semibold">
            New series
          </h3>
        </div>
        <div className="flex flex-col gap-3 p-4">
          <label className="flex flex-col gap-1 text-sm text-muted">
            Name
            <input
              ref={nameRef}
              data-testid={libraryTestIds.newSeriesName}
              autoComplete="off"
              value={name}
              aria-invalid={nameError !== null}
              onChange={(event) => setName(event.target.value)}
              className={field}
            />
          </label>
          {nameError && (
            <span
              role="alert"
              data-testid={libraryTestIds.newSeriesNameError}
              className="text-xs text-danger"
            >
              {nameError}
            </span>
          )}
          {nameTaken && (
            <span data-testid={libraryTestIds.newSeriesNotice} className="text-xs text-warn">
              A series with this name already exists. You can still create this one.
            </span>
          )}
          <fieldset className="m-0 flex flex-col gap-1 border-0 p-0">
            <legend className="mb-1 p-0 text-sm text-muted">
              Standalone books, in the order you pick them
            </legend>
            <div className="flex max-h-48 flex-col overflow-auto rounded border border-border">
              {standalone.length === 0 && (
                <span className="px-2 py-1 text-sm text-muted">No standalone books.</span>
              )}
              {standalone.map((book) => (
                <label key={book.id} className="flex items-center gap-2 px-2 py-1 text-sm">
                  <input
                    type="checkbox"
                    data-testid={libraryTestIds.newSeriesBook(book.id)}
                    checked={picked.includes(book.id)}
                    onChange={() => toggle(book.id)}
                  />
                  <span className="flex-1 truncate">{book.title}</span>
                  {picked.includes(book.id) && (
                    <span className="text-xs text-muted tabular-nums">
                      Book {String(picked.indexOf(book.id) + 1).padStart(3, '0')}
                    </span>
                  )}
                </label>
              ))}
            </div>
            <span data-testid={libraryTestIds.newSeriesPicked} className="text-xs text-muted">
              {picked.length === 0
                ? 'No books yet: you can add them later.'
                : picked.map(titleOf).join(' → ')}
            </span>
          </fieldset>
        </div>
        {saveError && (
          <p
            role="alert"
            data-testid={libraryTestIds.newSeriesError}
            className="m-0 px-4 pb-3 text-xs text-danger"
          >
            {saveError}
          </p>
        )}
        <div className="flex justify-end gap-2 border-t border-border bg-bg px-4 py-3">
          <button
            type="button"
            data-testid={libraryTestIds.newSeriesCancel}
            onClick={onClose}
            className={button}
          >
            Cancel
          </button>
          <button
            type="submit"
            data-testid={libraryTestIds.newSeriesCreate}
            disabled={busy}
            className="h-8 rounded border-0 bg-accent px-3 text-sm text-white"
          >
            Create series
          </button>
        </div>
      </form>
    </dialog>
  );
}
