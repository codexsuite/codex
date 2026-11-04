import { useRef, useState } from 'react';
import { useModalFocus } from '../../editing/useModalFocus';
import type { KbDeleteImpact } from '@codex/core/api/kbEntries';
import { kbTestIds } from './testIds';

interface DeleteEntryDialogProps {
  name: string;
  /** The counts, null while they load. */
  impact: KbDeleteImpact | null;
  /** Why the counts could not be loaded. */
  loadError: string | null;
  onCancel: () => void;
  /** Rejects with the server's message when the delete fails. */
  onDelete: () => Promise<unknown>;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

function describe(impact: KbDeleteImpact): string {
  if (impact.scenes === 0 && impact.notes === 0) return 'It is not used anywhere in the book.';
  const notes =
    impact.notes > 0
      ? `, and ${plural(impact.notes, 'timeline note is', 'timeline notes are')} deleted`
      : '';
  return `It is used in ${plural(impact.scenes, 'scene', 'scenes')}. Mentions turn into plain text, links and beat context are removed${notes}.`;
}

// Says how many scenes use the entry and what happens to them, then deletes on confirm.
export function DeleteEntryDialog({
  name,
  impact,
  loadError,
  onCancel,
  onDelete,
}: DeleteEntryDialogProps) {
  const dialog = useRef<HTMLDivElement>(null);
  useModalFocus(dialog);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const error = deleteError ?? loadError;

  const confirm = async () => {
    setBusy(true);
    try {
      await onDelete();
    } catch (e) {
      setDeleteError(e instanceof Error ? e.message : 'The entry could not be deleted.');
      setBusy(false);
    }
  };

  return (
    <div
      ref={dialog}
      role="dialog"
      aria-label={`Delete ${name}`}
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        onCancel();
      }}
      data-testid={kbTestIds.deleteDialog}
      className="absolute right-0 z-30 mt-1 w-72 rounded border border-border bg-surface p-3 shadow-lg outline-none"
    >
      <h4 className="m-0 text-sm font-semibold">Delete {name || 'this entry'}?</h4>
      <p data-testid={kbTestIds.deleteMessage} className="m-0 mt-1 text-sm text-muted">
        {impact ? describe(impact) : 'Checking where it is used…'}
      </p>
      {error && (
        <p
          data-testid={kbTestIds.deleteError}
          role="alert"
          className="m-0 mt-1 text-xs text-danger"
        >
          {error}
        </p>
      )}
      <div className="mt-2 flex justify-end gap-2">
        <button
          type="button"
          data-testid={kbTestIds.deleteCancel}
          onClick={onCancel}
          className="h-7 rounded border border-border bg-transparent px-3 text-sm text-text"
        >
          Cancel
        </button>
        <button
          type="button"
          data-testid={kbTestIds.deleteConfirm}
          disabled={impact === null || busy}
          onClick={() => void confirm()}
          className="h-7 rounded border-0 bg-danger px-3 text-sm text-white"
        >
          Delete
        </button>
      </div>
    </div>
  );
}
