import { useState } from 'react';
import { refsTestIds } from '../testIds';

// Recording a grant attaches the signed letter and the credit line the bibliography prints.
export function GrantForm({
  busy,
  onGrant,
  onCancel,
}: {
  busy: boolean;
  onGrant: (letter: File, creditLine: string, terms: string, fee: string) => void;
  onCancel: () => void;
}) {
  const [letter, setLetter] = useState<File | null>(null);
  const [creditLine, setCreditLine] = useState('');
  const [terms, setTerms] = useState('');
  const [fee, setFee] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  return (
    <form
      data-testid={refsTestIds.permGrantForm}
      className="flex flex-col gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        if (fee.trim() !== '' && !(Number(fee) >= 0)) {
          setProblem('The fee must be a number, zero or more.');
          return;
        }
        setProblem(null);
        if (letter !== null) onGrant(letter, creditLine, terms, fee);
      }}
    >
      <label className="flex flex-col gap-1 text-sm text-muted">
        Signed letter
        <input
          type="file"
          accept=".pdf,.png,.jpg,.jpeg,.txt,.doc,.docx,.eml"
          data-testid={refsTestIds.permFile}
          onChange={(event) => setLetter(event.target.files?.[0] ?? null)}
          className="text-sm text-text"
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-muted">
        Credit line printed with the citation
        <input
          type="text"
          value={creditLine}
          data-testid={refsTestIds.permCreditInput}
          onChange={(event) => setCreditLine(event.target.value)}
          className="rounded-sm border border-border bg-surface p-1 text-sm text-text"
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-muted">
        Terms (optional)
        <input
          type="text"
          value={terms}
          data-testid={refsTestIds.permTermsInput}
          onChange={(event) => setTerms(event.target.value)}
          className="rounded-sm border border-border bg-surface p-1 text-sm text-text"
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-muted">
        Fee (optional)
        <input
          type="text"
          inputMode="decimal"
          value={fee}
          data-testid={refsTestIds.permFeeInput}
          onChange={(event) => setFee(event.target.value)}
          className="rounded-sm border border-border bg-surface p-1 text-sm text-text"
        />
      </label>
      {problem !== null && (
        <p role="alert" data-testid={refsTestIds.permFormError} className="m-0 text-sm text-danger">
          {problem}
        </p>
      )}
      <div className="flex gap-2">
        <button
          type="submit"
          disabled={busy || letter === null}
          data-testid={refsTestIds.permGrantSubmit}
          className="self-start rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
        >
          Record grant
        </button>
        <button
          type="button"
          data-testid={refsTestIds.permGrantCancel}
          onClick={onCancel}
          className="rounded-sm border border-border px-3 py-1 text-sm text-text"
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
