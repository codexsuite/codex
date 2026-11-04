import type { ClearanceSummary } from '@codex/core/api/references';
import { lookOf, statusLooks, statusOrder, type StatusFilter } from './sourceStatus';
import { refsTestIds } from './testIds';

const counts = (summary: ClearanceSummary): Record<StatusFilter, number> => ({
  all: summary.total,
  risk: summary.risks,
  needs_check: summary.needsCheck,
  cleared: summary.cleared,
  not_cited: summary.notCited,
});

// The status filter: All, then one chip per clearance status with the summary's count.
export function StatusChips({
  summary,
  value,
  onChange,
}: {
  summary: ClearanceSummary;
  value: StatusFilter;
  onChange: (filter: StatusFilter) => void;
}) {
  const count = counts(summary);
  const filters: StatusFilter[] = ['all', ...statusOrder];
  return (
    <div
      role="group"
      aria-label="Filter by clearance"
      data-testid={refsTestIds.chipGroup}
      className="flex flex-wrap gap-1"
    >
      {filters.map((filter) => (
        <button
          key={filter}
          type="button"
          aria-pressed={value === filter}
          data-testid={refsTestIds.chip(filter)}
          onClick={() => onChange(filter)}
          className="inline-flex h-6 cursor-pointer items-center gap-[5px] rounded-full border border-border bg-surface px-2 text-xs font-medium whitespace-nowrap text-text tabular-nums hover:bg-surface-2 aria-pressed:border-[color-mix(in_srgb,var(--color-accent)_40%,var(--color-border))] aria-pressed:bg-accent-soft"
        >
          {filter !== 'all' && (
            <i className={`size-1.5 rounded-full ${lookOf(filter)?.dot ?? ''}`} aria-hidden />
          )}
          {filter === 'all' ? 'All' : statusLooks[filter].chip}
          <span data-testid={refsTestIds.chipCount(filter)} className="text-muted">
            {count[filter]}
          </span>
        </button>
      ))}
    </div>
  );
}
