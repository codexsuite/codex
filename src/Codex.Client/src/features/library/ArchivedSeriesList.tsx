import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { seriesApi } from '@codex/core/api/library';
import type { LibrarySeries } from '@codex/core/api/types';
import { libraryTestIds } from './testIds';

const button =
  'h-6 cursor-pointer rounded border border-border bg-surface px-2 text-xs text-text hover:bg-surface-2';

function ArchivedSeriesRow({ series }: { series: LibrarySeries }) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const restore = async () => {
    setError(null);
    try {
      await seriesApi.restore(series.id);
      await queryClient.invalidateQueries({ queryKey: ['library'] });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The series could not be restored.');
    }
  };
  return (
    <li
      data-testid={libraryTestIds.archivedSeries(series.id)}
      className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded border border-border bg-surface px-3 py-2"
    >
      <span className="text-sm font-semibold text-text">{series.name}</span>
      <span className="text-xs text-muted">
        Series · {series.books.map((b) => b.title).join(', ') || 'no books'}
      </span>
      <button
        type="button"
        data-testid={libraryTestIds.restoreSeries(series.id)}
        onClick={() => void restore()}
        className={`${button} ml-auto`}
      >
        Restore series
      </button>
      {error && (
        <span role="alert" className="basis-full text-xs text-danger">
          {error}
        </span>
      )}
    </li>
  );
}

// The Archived view's archived series, one row each with its books named; Restore series brings the books back too.
export function ArchivedSeriesList({ series }: { series: LibrarySeries[] }) {
  return (
    <ul className="m-0 flex list-none flex-col gap-2 px-3 pt-3 pb-1">
      {series.map((s) => (
        <ArchivedSeriesRow key={s.id} series={s} />
      ))}
    </ul>
  );
}
