import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { getKb, kbKey } from '@codex/core/api/kb';
import { getKbMergeImpact, type KbMergeImpact } from '@codex/core/api/kbEntries';
import { kbTestIds } from './testIds';

interface MergeEntryDialogProps {
  bookId: string;
  entryId: string;
  name: string;
  onCancel: () => void;
  /** Merges into the picked entry; rejects with the server's message. */
  onMerge: (intoId: string, intoName: string) => Promise<unknown>;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

const quoted = (names: string[]) => names.map((n) => `"${n}"`).join(', ');

export function describeMerge(impact: KbMergeImpact): string {
  const counts = `${plural(impact.sceneLinks, 'scene link', 'scene links')}, ${plural(impact.additions, 'addition', 'additions')}, ${impact.related} related.`;
  if (impact.newAliases.length === 0) return counts;
  return `${counts} ${quoted(impact.newAliases)} ${impact.newAliases.length === 1 ? 'becomes an alias' : 'become aliases'}.`;
}

// Merge a duplicate into another entry: pick the target (never the entry itself), read what moves, then confirm.
// Nothing is written until Merge.
export function MergeEntryDialog({
  bookId,
  entryId,
  name,
  onCancel,
  onMerge,
}: MergeEntryDialogProps) {
  const kb = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  const [target, setTarget] = useState<{ id: string; name: string } | null>(null);
  const [impact, setImpact] = useState<KbMergeImpact | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const options = (kb.data?.entries ?? []).filter((e) => e.id !== entryId);

  const pick = (id: string, intoName: string) => {
    setTarget({ id, name: intoName });
    setImpact(null);
    setError(null);
    getKbMergeImpact(entryId, id).then(setImpact, (e: unknown) =>
      setError(e instanceof Error ? e.message : 'Could not load what the merge moves.'),
    );
  };
  const confirm = async () => {
    if (!target) return;
    setBusy(true);
    try {
      await onMerge(target.id, target.name);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The entries could not be merged.');
      setBusy(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-label={`Merge ${name}`}
      data-testid={kbTestIds.mergeDialog}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation();
          onCancel();
        }
      }}
      className="absolute right-0 z-30 mt-1 w-72 rounded border border-border bg-surface p-3 shadow-lg"
    >
      <h4 className="m-0 text-sm font-semibold">Merge {name || 'this entry'} into…</h4>
      <ul className="m-0 mt-1 max-h-44 list-none overflow-auto p-0">
        {options.map((option) => (
          <li key={option.id}>
            <button
              type="button"
              data-testid={kbTestIds.mergeOption(option.id)}
              aria-pressed={target?.id === option.id}
              onClick={() => pick(option.id, option.name)}
              className="flex h-7 w-full items-center rounded-sm border-0 bg-transparent px-2 text-left text-sm text-text hover:bg-accent-soft aria-pressed:bg-accent-soft aria-pressed:font-medium"
            >
              {option.name}
            </button>
          </li>
        ))}
      </ul>
      {target && (
        <p data-testid={kbTestIds.mergeMessage} className="m-0 mt-2 text-sm text-muted">
          {impact ? describeMerge(impact) : 'Checking what moves…'}
        </p>
      )}
      {error && (
        <p data-testid={kbTestIds.mergeError} role="alert" className="m-0 mt-1 text-xs text-danger">
          {error}
        </p>
      )}
      <div className="mt-2 flex justify-end gap-2">
        <button
          type="button"
          autoFocus
          data-testid={kbTestIds.mergeCancel}
          onClick={onCancel}
          className="h-7 rounded border border-border bg-transparent px-3 text-sm text-text"
        >
          Cancel
        </button>
        <button
          type="button"
          data-testid={kbTestIds.mergeConfirm}
          disabled={impact === null || busy}
          onClick={() => void confirm()}
          className="h-7 rounded border-0 bg-accent px-3 text-sm text-white disabled:opacity-50"
        >
          Merge
        </button>
      </div>
    </div>
  );
}
