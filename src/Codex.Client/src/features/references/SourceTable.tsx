import { Fragment } from 'react';
import type { SourceRow } from '@codex/core/api/referenceSources';
import { allColumns, columnClass, columnLabels } from './columns';
import { groupSources, type Grouping } from './sourceGroups';
import { SourceGroupRow } from './SourceGroupRow';
import { SourceTableRow } from './SourceTableRow';
import { refsTestIds } from './testIds';

const headClass =
  'sticky top-0 z-[2] h-7 border-b border-border bg-surface-2 px-2 text-xs font-medium whitespace-nowrap text-muted';

// The Library list-view pattern: one row per source under optional group headings. `sources` is already filtered.
export function SourceTable({
  sources,
  grouping,
  selectedId,
  onSelect,
  filtered,
  onClearFilters,
}: {
  sources: SourceRow[];
  grouping: Grouping;
  selectedId: string | undefined;
  onSelect: (sourceId: string) => void;
  filtered: boolean;
  onClearFilters: () => void;
}) {
  if (sources.length === 0) {
    return (
      <div data-testid={refsTestIds.empty} className="px-4 py-15 text-center text-base text-muted">
        No sources match.{' '}
        {filtered && (
          <button
            type="button"
            data-testid={refsTestIds.clearFilters}
            onClick={onClearFilters}
            className="text-accent underline"
          >
            Clear filters
          </button>
        )}
      </div>
    );
  }

  const span = 3 + allColumns.length;
  return (
    <table
      data-testid={refsTestIds.table}
      className="w-full border-separate border-spacing-0 text-sm"
    >
      <thead>
        <tr>
          <th
            className={`${headClass} w-10 !pl-3 text-left`}
            title="Marker number: order of first use in the book"
          >
            #
          </th>
          <th className={`${headClass} text-left`}>Source</th>
          {allColumns.map((column) => (
            <th
              key={column}
              data-testid={refsTestIds.columnHead(column)}
              className={`${headClass} ${columnClass[column]} ${column === 'quoted' ? 'text-right' : 'text-left'}`}
            >
              {columnLabels[column]}
            </th>
          ))}
          <th className={`${headClass} w-[210px] text-left`}>Clearance</th>
        </tr>
      </thead>
      <tbody>
        {groupSources(sources, grouping).map((group) => (
          <Fragment key={group.name ?? 'all'}>
            {group.name !== null && (
              <SourceGroupRow
                name={group.name}
                count={group.sources.length}
                dot={group.dot}
                span={span}
              />
            )}
            {group.sources.map((source) => (
              <SourceTableRow
                key={source.id}
                source={source}
                selected={source.id === selectedId}
                onSelect={() => onSelect(source.id)}
              />
            ))}
          </Fragment>
        ))}
      </tbody>
    </table>
  );
}
