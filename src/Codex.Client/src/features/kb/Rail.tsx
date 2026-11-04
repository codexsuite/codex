import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { KbEntry } from '@codex/core/api/kbEntries';
import { referencesLink } from '@codex/core/shell/links';
import { railHeading } from './kbStyles';
import { RelatedSection } from './RelatedSection';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface RailProps {
  bookId: string;
  entry: KbEntry;
  /** The Details part (fields the author edits), drawn first. */
  details: ReactNode;
  onAlwaysInContext: (value: boolean) => void;
}

const statusWords: Record<string, string> = {
  cleared: 'Cleared',
  needs_check: 'Needs check',
  risk: 'Risk',
  not_cited: 'Not cited',
};

const statusTone: Record<string, string> = {
  cleared: 'text-ok',
  needs_check: 'text-warn',
  risk: 'text-danger',
  not_cited: 'text-muted',
};

// The right-hand rail: Details, context switch, Source, Related and the dates, each section under a top rule on a
// lightly tinted ground. The reference id sits beside the tabs, not here.
export function Rail({ bookId, entry, details, onAlwaysInContext }: RailProps) {
  const canWrite = useCanWrite();
  return (
    <aside
      aria-label="Entry properties"
      className="flex min-w-0 flex-col self-stretch border-l border-border bg-[color-mix(in_srgb,var(--color-surface-2)_30%,var(--color-surface))] pt-4 pr-4 pb-5 pl-3 sticky top-0 @max-[680px]:static @max-[680px]:border-t @max-[680px]:border-l-0 @max-[680px]:pl-4 [&>*+*]:mt-4 [&>*+*]:border-t [&>*+*]:border-border [&>*+*]:pt-3"
    >
      {details}
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          data-testid={kbTestIds.alwaysInContext}
          checked={entry.alwaysInContext}
          disabled={!canWrite}
          onChange={(event) => onAlwaysInContext(event.target.checked)}
        />
        Always in context
      </label>
      <section
        data-testid={kbTestIds.source}
        aria-label="Source"
        className="flex flex-col gap-[3px]"
      >
        <h4 className={`${railHeading} mb-1.5`}>Source</h4>
        {entry.source ? (
          <>
            <span className="text-sm">{entry.source.title}</span>
            <span className={`text-xs ${statusTone[entry.source.status] ?? 'text-muted'}`}>
              {statusWords[entry.source.status] ?? entry.source.status}
            </span>
            <Link
              to={referencesLink(bookId, { source: entry.source.id })}
              className="text-xs font-medium text-accent no-underline hover:underline"
            >
              Open in References
            </Link>
          </>
        ) : (
          <span className="text-xs text-muted italic">No source linked.</span>
        )}
      </section>
      <RelatedSection bookId={bookId} entry={entry} />
      <div className="text-xs leading-[1.6] text-muted">
        Created {entry.createdLabel} · Edited {entry.updatedLabel}
      </div>
    </aside>
  );
}
