import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { kbKey } from '@codex/core/api/kb';
import { kbEntryKey, promoteKbEntry } from '@codex/core/api/kbEntries';
import { showToast } from '@codex/core/editing/toastStore';
import { kbTestIds } from './testIds';

interface PromoteDialogProps {
  bookId: string;
  entryId: string;
  name: string;
  seriesName: string;
  onCancel: () => void;
  /** `merged`: the entry was folded into a same-named series entry, so this one no longer exists. */
  onDone: (merged: boolean) => void;
}

// "Move to series": asks first, then moves. When the series already has an entry with the name, nothing is written
// until Merge into the series entry is chosen.
export function PromoteDialog({
  bookId,
  entryId,
  name,
  seriesName,
  onCancel,
  onDone,
}: PromoteDialogProps) {
  const queryClient = useQueryClient();
  const [clash, setClash] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (merge: boolean) => {
    setBusy(true);
    setError(null);
    try {
      await promoteKbEntry(entryId, merge);
      if (merge) queryClient.removeQueries({ queryKey: kbEntryKey(entryId) });
      await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
      await queryClient.invalidateQueries({ queryKey: ['kb-entry'] });
      showToast({ message: `${name} moved to ${seriesName}.` });
      onDone(merge);
    } catch (e) {
      if (e instanceof ApiRequestError && e.code === 'needs_confirmation') setClash(true);
      else setError(e instanceof Error ? e.message : 'The entry could not be moved.');
      setBusy(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-label={`Move ${name} to series`}
      data-testid={kbTestIds.promoteDialog}
      className="absolute right-0 z-30 mt-1 w-72 rounded border border-border bg-surface p-3 shadow-lg"
    >
      <h4 className="m-0 text-sm font-semibold">Move {name || 'this entry'} to the series?</h4>
      <p data-testid={kbTestIds.promoteMessage} className="m-0 mt-1 text-sm text-muted">
        {clash
          ? `${seriesName} already has an entry named ${name}. Merge this one into it, or cancel.`
          : `Every book in ${seriesName} will see it. Mentions and links keep working.`}
      </p>
      {error && (
        <p
          data-testid={kbTestIds.promoteError}
          role="alert"
          className="m-0 mt-1 text-xs text-danger"
        >
          {error}
        </p>
      )}
      <div className="mt-2 flex justify-end gap-2">
        <button
          type="button"
          data-testid={kbTestIds.promoteCancel}
          onClick={onCancel}
          className="h-7 rounded border border-border bg-transparent px-3 text-sm text-text"
        >
          Cancel
        </button>
        {clash ? (
          <button
            type="button"
            data-testid={kbTestIds.promoteMerge}
            disabled={busy}
            onClick={() => void run(true)}
            className="h-7 rounded border-0 bg-accent px-3 text-sm text-white"
          >
            Merge into the series entry
          </button>
        ) : (
          <button
            type="button"
            data-testid={kbTestIds.promoteConfirm}
            disabled={busy}
            onClick={() => void run(false)}
            className="h-7 rounded border-0 bg-accent px-3 text-sm text-white"
          >
            Move
          </button>
        )}
      </div>
    </div>
  );
}
