import { useState } from 'react';
import { refsTestIds } from '../testIds';

// A refusal keeps the risk; the note says why they declined, if they said.
export function RefuseForm({
  busy,
  onRefuse,
}: {
  busy: boolean;
  onRefuse: (note: string) => void;
}) {
  const [note, setNote] = useState('');
  return (
    <form
      data-testid={refsTestIds.permRefuseForm}
      className="flex flex-col gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        onRefuse(note);
      }}
    >
      <label className="flex flex-col gap-1 text-sm text-muted">
        What did they say? (optional)
        <textarea
          rows={2}
          value={note}
          data-testid={refsTestIds.permRefuseNote}
          onChange={(event) => setNote(event.target.value)}
          className="rounded-sm border border-border bg-surface p-2 text-sm text-text"
        />
      </label>
      <button
        type="submit"
        disabled={busy}
        data-testid={refsTestIds.permRefuseSubmit}
        className="self-start rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
      >
        Record refusal
      </button>
    </form>
  );
}
