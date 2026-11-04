import { useState } from 'react';
import type { SourceDetail, SourceRow } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

// Merge a duplicate into another source: its details fill what the target lacks, its uses move, and it is archived.
export function MergeSource({
  source,
  candidates,
  busy,
  onCancel,
  onConfirm,
}: {
  source: SourceDetail;
  candidates: SourceRow[];
  busy: boolean;
  onCancel: () => void;
  onConfirm: (intoId: string) => void;
}) {
  const options = candidates.filter((candidate) => candidate.id !== source.id);
  const [target, setTarget] = useState('');
  const uses = source.uses.length;
  return (
    <div
      role="dialog"
      aria-label="Merge into another source"
      data-testid={refsTestIds.mergeDialog}
      className="flex flex-col gap-2 rounded-md border border-border p-3 text-sm"
    >
      <label className="flex flex-col gap-1 text-muted">
        Merge {source.title} into
        <select
          value={target}
          data-testid={refsTestIds.mergeTarget}
          onChange={(event) => setTarget(event.target.value)}
          className="rounded-sm border border-border bg-surface p-1 text-text"
        >
          <option value="">Choose a source</option>
          {options.map((option) => (
            <option key={option.id} value={option.id}>
              {option.title}
              {option.year === null ? '' : ` (${option.year})`}
            </option>
          ))}
        </select>
      </label>
      <p data-testid={refsTestIds.mergeSummary} className="m-0 text-text">
        1 source&apos;s details and {uses} {uses === 1 ? 'use' : 'uses'} move; the duplicate is
        archived.
      </p>
      <div className="flex gap-2">
        <button
          type="button"
          disabled={busy || target === ''}
          data-testid={refsTestIds.mergeConfirm}
          onClick={() => onConfirm(target)}
          className="rounded-sm bg-accent px-3 py-1 text-sm font-medium text-surface disabled:opacity-50"
        >
          Merge
        </button>
        <button
          type="button"
          data-testid={refsTestIds.mergeCancel}
          onClick={onCancel}
          className="rounded-sm border border-border px-3 py-1 text-sm"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
