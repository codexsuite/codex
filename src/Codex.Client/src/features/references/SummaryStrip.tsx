import type { ReactNode } from 'react';
import type { ClearanceSummary } from '@codex/core/api/references';
import { RefIcon } from './RefIcon';
import { statusLooks } from './sourceStatus';
import { StatusChips } from './StatusChips';
import type { StatusFilter } from './sourceStatus';
import { refsTestIds } from './testIds';

// The meter runs from cleared to risk, with the uncited sources last.
const meterOrder = ['cleared', 'needs_check', 'risk', 'not_cited'] as const;

// Stacked meter by status, "n of m cited sources cleared", whether risks block print export, the status chips, and
// whatever the page puts after them (the unflagged-quotes button).
export function SummaryStrip({
  summary,
  filter,
  onFilter,
  children,
}: {
  summary: ClearanceSummary;
  filter: StatusFilter;
  onFilter: (filter: StatusFilter) => void;
  children?: ReactNode;
}) {
  const cited = summary.total - summary.notCited;
  const share: Record<(typeof meterOrder)[number], number> = {
    risk: summary.risks,
    needs_check: summary.needsCheck,
    cleared: summary.cleared,
    not_cited: summary.notCited,
  };
  return (
    <div
      data-testid={refsTestIds.strip}
      className="flex flex-wrap items-center gap-x-4 gap-y-1.5 border-b border-border bg-surface px-3 py-[7px] text-xs"
    >
      <span
        role="img"
        data-testid={refsTestIds.meter}
        aria-label={`${summary.cleared} cleared, ${summary.needsCheck} need a check, ${summary.risks} at risk, ${summary.notCited} not cited`}
        className="flex h-1.5 w-40 shrink-0 overflow-hidden rounded-[3px] bg-surface-2"
      >
        {meterOrder
          .filter((status) => share[status] > 0)
          .map((status, index) => (
            <i
              key={status}
              // A 1px surface gap between segments.
              className={`block h-full ${index > 0 ? 'shadow-[-1px_0_0_var(--color-surface)]' : ''} ${statusLooks[status].meter}`}
              style={{ width: `${(share[status] / summary.total) * 100}%` }}
            />
          ))}
      </span>
      <span data-testid={refsTestIds.clearedText} className="whitespace-nowrap tabular-nums">
        <b className="font-semibold">
          {summary.cleared} of {cited}
        </b>{' '}
        cited sources cleared
      </span>
      {summary.risks > 0 ? (
        <span
          data-testid={refsTestIds.gate}
          data-blocked="true"
          title="Export for printer checks References first"
          className="inline-flex items-center gap-[5px] whitespace-nowrap text-danger"
        >
          <RefIcon name="warn" className="size-3" />
          {summary.risks} {summary.risks === 1 ? 'risk blocks' : 'risks block'} print export
        </span>
      ) : (
        <span
          data-testid={refsTestIds.gate}
          data-blocked="false"
          className="inline-flex items-center gap-[5px] whitespace-nowrap text-ok"
        >
          <RefIcon name="check" className="size-3" />
          Ready for print export
        </span>
      )}
      {summary.remindersDue > 0 && (
        <span
          data-testid={refsTestIds.remindersChip}
          title="A permission request has waited past its reminder date"
          className="inline-flex items-center gap-[5px] whitespace-nowrap text-warn"
        >
          <RefIcon name="mail" className="size-3" />
          {summary.remindersDue} {summary.remindersDue === 1 ? 'reply' : 'replies'} overdue
        </span>
      )}
      <span className="flex-1" />
      <StatusChips summary={summary} value={filter} onChange={onFilter} />
      {children}
    </div>
  );
}
