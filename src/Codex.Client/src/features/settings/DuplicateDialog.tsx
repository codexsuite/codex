import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { settingsApi, type BookSettings, type DuplicatedBook } from '@codex/core/api/settings';
import { settingsTestIds } from './testIds';
import { useReturnFocus } from '../library/useReturnFocus';

interface DuplicateDialogProps {
  settings: BookSettings;
  onClose: () => void;
  onDuplicated: (copy: DuplicatedBook) => void;
  /** A second edition: the same copy, but the sources come along with their sign-offs cleared. */
  edition?: boolean;
}

// Asks for the copy's title (default "<title> (copy)"), then duplicates. References start empty.
export function DuplicateDialog({
  settings,
  onClose,
  onDuplicated,
  edition = false,
}: DuplicateDialogProps) {
  const queryClient = useQueryClient();
  useReturnFocus();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [title, setTitle] = useState(
    edition ? `${settings.title} (2nd edition)` : `${settings.title} (copy)`,
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      const copy = await (edition
        ? settingsApi.newEdition(settings.id, title)
        : settingsApi.duplicate(settings.id, title));
      await queryClient.invalidateQueries({ queryKey: ['library'] });
      onDuplicated(copy);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The book could not be duplicated.');
      setBusy(false);
    }
  };

  return (
    <dialog
      ref={dialogRef}
      data-testid={settingsTestIds.duplicateDialog}
      aria-labelledby="settings-duplicate-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[min(480px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-3 p-4">
        <h3 id="settings-duplicate-title" className="m-0 text-md font-semibold">
          {edition ? 'Start a new edition' : 'Duplicate book'}
        </h3>
        <label className="flex flex-col gap-1 text-sm text-muted">
          Title
          <input
            data-testid={settingsTestIds.duplicateTitle}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="h-8 rounded border border-border bg-bg px-2 text-base text-text"
          />
        </label>
        <p className="m-0 text-xs text-muted">
          Copies the structure, prose, beats, this book’s Knowledge base and labels, and these
          settings
          {settings.seriesName ? `. It stays in ${settings.seriesName} as the last book` : ''}.
          {edition
            ? 'The sources come along with their uses; sign-offs and permission requests start clear, because they cover one book’s print run.'
            : 'References start empty: permission and sign-off cover one book’s print run.'}
        </p>
        {error && (
          <p
            data-testid={settingsTestIds.duplicateError}
            role="alert"
            className="m-0 text-sm text-danger"
          >
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            data-testid={settingsTestIds.duplicateCancel}
            onClick={onClose}
            className="h-8 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.duplicateConfirm}
            disabled={busy || title.trim() === ''}
            onClick={() => void confirm()}
            className="h-8 cursor-pointer rounded border-0 bg-accent px-3 text-base text-white disabled:opacity-50"
          >
            {edition ? 'Create' : 'Duplicate'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
