import { useState } from 'react';
import { refsTestIds } from '../testIds';

// "Accept as fair use" needs a reason, which the source keeps.
export function FairUseForm({
  busy,
  onAccept,
}: {
  busy: boolean;
  onAccept: (reason: string) => void;
}) {
  const [reason, setReason] = useState('');
  return (
    <form
      data-testid={refsTestIds.fairUseForm}
      className="mt-2 flex flex-col gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        onAccept(reason);
      }}
    >
      <label className="text-sm text-muted" htmlFor="refs-fair-use-reason">
        Why is this fair use?
      </label>
      <textarea
        id="refs-fair-use-reason"
        data-testid={refsTestIds.fairUseReason}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        rows={2}
        className="rounded-sm border border-border bg-surface p-2 text-sm text-text"
      />
      <button
        type="submit"
        disabled={busy || reason.trim() === ''}
        data-testid={refsTestIds.fairUseAccept}
        className="self-start rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
      >
        Accept as fair use
      </button>
    </form>
  );
}
