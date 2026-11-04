import type { SourceRow } from '@codex/core/api/referenceSources';
import { lookOf, statusLooks, statusOrder, type StatusFilter } from './sourceStatus';

export type Grouping = 'status' | 'kind' | 'none';

export interface SourceGroup {
  name: string | null;
  dot?: string;
  sources: SourceRow[];
}

/** The rows a status chip lets through. */
export const filterSources = (sources: SourceRow[], filter: StatusFilter) =>
  filter === 'all' ? sources : sources.filter((s) => s.status === filter);

/** The rows whose title, authors or publisher contain the search text (any case). */
export function searchSources(sources: SourceRow[], query: string): SourceRow[] {
  const wanted = query.trim().toLowerCase();
  if (wanted === '') return sources;
  return sources.filter((s) =>
    [s.title, s.authors, s.publisher ?? ''].join(' ').toLowerCase().includes(wanted),
  );
}

/** Groups the rows (already in marker order): by status with risk first, by kind, or one nameless group. Empty groups are dropped. */
export function groupSources(sources: SourceRow[], grouping: Grouping): SourceGroup[] {
  if (grouping === 'none') return [{ name: null, sources }];
  if (grouping === 'status') {
    return statusOrder
      .map((status) => ({
        name: statusLooks[status].label,
        dot: lookOf(status)?.dot,
        sources: sources.filter((s) => s.status === status),
      }))
      .filter((group) => group.sources.length > 0);
  }
  const kinds = [...new Set(sources.map((s) => s.kindLabel))].sort((a, b) => a.localeCompare(b));
  return kinds.map((kind) => ({
    name: kind,
    sources: sources.filter((s) => s.kindLabel === kind),
  }));
}
