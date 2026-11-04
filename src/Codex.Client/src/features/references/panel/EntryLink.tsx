import { useQuery } from '@tanstack/react-query';
import { getKb, kbKey } from '@codex/core/api/kb';
import type { SourceEntry } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

// The "Codex" link: the knowledge base entry this source belongs to. Entries are those in the book's scope.
export function EntryLink({
  bookId,
  entry,
  busy,
  onChange,
}: {
  bookId: string;
  entry: SourceEntry | null;
  busy: boolean;
  onChange: (entryId: string | null) => void;
}) {
  const kb = useQuery({
    queryKey: kbKey(bookId),
    queryFn: ({ signal }) => getKb(bookId, { signal }),
  });
  const entries = [...(kb.data?.entries ?? [])].sort((a, b) => a.name.localeCompare(b.name));
  return (
    <select
      aria-label="Linked Codex entry"
      data-testid={refsTestIds.entry}
      value={entry?.id ?? ''}
      disabled={busy || kb.data === undefined}
      onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
      className="min-w-0 flex-1 rounded-sm border border-border bg-surface px-2 py-1 text-sm text-text"
    >
      <option value="">No linked entry</option>
      {entries.map((candidate) => (
        <option key={candidate.id} value={candidate.id}>
          {candidate.name}
        </option>
      ))}
    </select>
  );
}
