import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { lifecycleApi } from '@codex/core/api/library';
import { showToast } from '@codex/core/editing/toastStore';
import { settingsLink } from '@codex/core/shell/links';
import { libraryTestIds } from './testIds';
import { useReturnFocus } from './useReturnFocus';

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

// J17 step 7: what goes with the book, then the title typed to enable Delete. Only an archived book gets here.
export function DeleteBookDialog({
  bookId,
  title,
  onClose,
}: {
  bookId: string;
  title: string;
  onClose: () => void;
}) {
  useReturnFocus();
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [typed, setTyped] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const impact = useQuery({
    queryKey: ['library', 'book-delete-impact', bookId],
    queryFn: () => lifecycleApi.bookImpact(bookId),
  });

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await lifecycleApi.deleteBook(bookId);
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      showToast({ message: `${title} deleted.` });
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The book could not be deleted.');
      setBusy(false);
    }
  };

  const data = impact.data;
  return (
    <dialog
      ref={dialogRef}
      data-testid={libraryTestIds.deleteBookDialog}
      aria-labelledby="delete-book-heading"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="delete-book-heading" className="m-0 text-md font-semibold">
          Delete {title} permanently?
        </h3>
        <p data-testid={libraryTestIds.deleteBookImpact} className="m-0 text-sm">
          {data
            ? `This removes ${plural(data.chapters, 'chapter', 'chapters')}, ${plural(data.scenes, 'scene', 'scenes')}, ${data.words.toLocaleString('en-US')} words, ${plural(data.sources, 'source', 'sources')}, and its Knowledge base. It cannot be undone.`
            : 'Checking what is in the book…'}
        </p>
        <p className="m-0 text-xs text-muted">
          <Link
            to={settingsLink(bookId, 'archive')}
            data-testid={libraryTestIds.deleteBookExport}
            className="text-accent"
          >
            Export source first
          </Link>{' '}
          if you want a copy.
        </p>
        <label className="flex flex-col gap-1 text-sm">
          Type the title to confirm
          <input
            data-testid={libraryTestIds.deleteBookTitle}
            autoComplete="off"
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            className="rounded border border-border bg-bg px-2 py-1 text-base text-text"
          />
        </label>
        {error && (
          <p
            role="alert"
            data-testid={libraryTestIds.deleteBookError}
            className="m-0 text-xs text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={libraryTestIds.deleteBookCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={libraryTestIds.deleteBookConfirm}
            disabled={busy || data === undefined || typed !== title}
            onClick={() => void confirm()}
            className="h-8 cursor-pointer rounded border-0 bg-danger px-3 text-base text-white disabled:opacity-50"
          >
            Delete
          </button>
        </div>
      </div>
    </dialog>
  );
}
