import type { LibrarySeries } from '@codex/core/api/types';
import { seriesTestIds } from './testIds';

function Stat({ testId, value, label }: { testId: string; value: string; label: string }) {
  return (
    <div data-testid={testId} className="flex flex-col">
      <b className="text-md font-semibold text-text tabular-nums">{value}</b>
      <span className="text-2xs tracking-[.05em] text-muted uppercase">{label}</span>
    </div>
  );
}

// The series' totals, from the library DTO (archived books are already excluded by the server).
export function SeriesStats({ series }: { series: LibrarySeries }) {
  return (
    <div data-testid={seriesTestIds.stats} className="flex flex-wrap gap-[18px]">
      <Stat testId={seriesTestIds.statBooks} value={String(series.bookCount)} label="Books" />
      <Stat
        testId={seriesTestIds.statWords}
        value={series.wordCount.toLocaleString('en-US')}
        label="Words"
      />
      <Stat
        testId={seriesTestIds.statPublished}
        value={String(series.publishedCount)}
        label="Published"
      />
      <Stat testId={seriesTestIds.statLastEdit} value={series.lastEditedLabel} label="Last edit" />
    </div>
  );
}
