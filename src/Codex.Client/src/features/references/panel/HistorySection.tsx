import type { SourceHistoryEntry } from '@codex/core/api/referenceSources';
import { refsTestIds } from '../testIds';

// What happened to the source, newest first.
export function HistorySection({ history }: { history: SourceHistoryEntry[] }) {
  if (history.length === 0) return <p className="m-0 text-sm text-muted">Nothing recorded yet.</p>;
  return (
    <ul data-testid={refsTestIds.history} className="m-0 flex list-none flex-col gap-1 p-0 text-sm">
      {history.map((entry, index) => (
        <li key={index} data-testid={refsTestIds.historyItem} className="flex gap-2">
          <time className="w-16 shrink-0 text-muted">{entry.when}</time>
          <span>
            <b className="font-medium">{entry.who}</b> {entry.text}
          </span>
        </li>
      ))}
    </ul>
  );
}
