import { useEffect, useRef } from 'react';
import { importTestIds } from '../library/importTestIds';
import { useReturnFocus } from '../library/useReturnFocus';

// Asked before Undo import deletes the book: the delete cannot be undone.
export function ImportBannerConfirm({
  fileName,
  busy,
  onConfirm,
  onCancel,
}: {
  fileName: string;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  useReturnFocus();
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  return (
    <dialog
      ref={dialogRef}
      data-testid={importTestIds.undoConfirm}
      aria-labelledby="import-undo-title"
      onCancel={(event) => {
        event.preventDefault();
        if (!busy) onCancel();
      }}
      className="m-auto w-[min(420px,100vw)] max-w-full rounded-lg border border-border bg-surface p-0 text-text shadow-lg backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-2 p-4">
        <h3 id="import-undo-title" className="m-0 text-md font-semibold">
          Undo import?
        </h3>
        <p className="m-0 text-sm">
          Undo import deletes the book and everything in it: the acts, chapters, scenes, prose and
          knowledge base entries imported from <b>{fileName}</b>, and anything added since. This
          cannot be undone.
        </p>
      </div>
      <div className="flex items-center justify-end gap-2 border-t border-border bg-bg px-4 py-3">
        <button
          type="button"
          data-testid={importTestIds.undoConfirmCancel}
          disabled={busy}
          onClick={onCancel}
          className="h-8 rounded border border-border bg-transparent px-3 text-sm text-text"
        >
          Keep the book
        </button>
        <button
          type="button"
          data-testid={importTestIds.undoConfirmDelete}
          disabled={busy}
          onClick={onConfirm}
          className="h-8 rounded border-0 bg-warn px-3 text-sm text-white disabled:opacity-60"
        >
          {busy ? 'Deleting…' : 'Delete the book'}
        </button>
      </div>
    </dialog>
  );
}
