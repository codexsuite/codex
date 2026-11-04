import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { getSources, sourcesKey } from '@codex/core/api/referenceSources';
import { clearanceSummaryKey, getClearanceSummary } from '@codex/core/api/references';
import { ErrorState } from '../../shell/ErrorState';
import { referencesLink } from '@codex/core/shell/links';
import { SlotFill } from '../../shell/Slots';
import { useDeepLink } from '../../shell/useDeepLink';
import { testIds } from '../../testIds';
import { AddSourceControls } from './add/AddSourceControls';
import { BibliographyView } from './BibliographyView';
import { QuoteFlagsButton } from './flags/QuoteFlagsButton';
import { QuoteFlagsInbox } from './flags/QuoteFlagsInbox';
import { RefIcon } from './RefIcon';
import { filterSources, searchSources, type Grouping } from './sourceGroups';
import { SourcePanel } from './panel/SourcePanel';
import { SourceTable } from './SourceTable';
import type { StatusFilter } from './sourceStatus';
import { SummaryStrip } from './SummaryStrip';
import { refsTestIds } from './testIds';
import { ReturnBar } from './ReturnBar';
import { useReturnTrip } from './useReturnTrip';
import { ViewToggle, type ReferencesView } from './ViewToggle';
import { useCanWrite } from '../../shell/bookAccess';

const groupings: { value: Grouping; label: string }[] = [
  { value: 'status', label: 'Status' },
  { value: 'kind', label: 'Kind' },
  { value: 'none', label: 'None (book order)' },
];

// The References screen: the toolbar, the summary strip, then the sources table beside the detail panel (or the
// bibliography). `#<source>` selects a row; the unflagged-quotes list takes the panel's place while it is open.
export function ReferencesPage() {
  const { bookId = '' } = useParams();
  const deepLink = useDeepLink();
  const navigate = useNavigate();
  const [view, setView] = useState<ReferencesView>('sources');
  const [filter, setFilter] = useState<StatusFilter>('all');
  const [query, setQuery] = useState('');
  const [grouping, setGrouping] = useState<Grouping>('status');
  // The unflagged-quotes list stays open only on the link it was opened on: following a link to a source shows it.
  const [flagsOpenOn, setFlagsOpenOn] = useState<string | null>(null);
  const returning = useReturnTrip();
  // Below write (#80): no Add or Import, and no unflagged-quotes list (each item there is a change).
  const canWrite = useCanWrite();

  const summary = useQuery({
    queryKey: clearanceSummaryKey(bookId),
    queryFn: ({ signal }) => getClearanceSummary(bookId, { signal }),
  });
  const sources = useQuery({
    queryKey: sourcesKey(bookId),
    queryFn: ({ signal }) => getSources(bookId, { signal }),
  });

  if (!deepLink.ok) {
    return (
      <ErrorState
        testId={testIds.shell.errorBadLink}
        title="This link doesn't make sense"
        detail={deepLink.error}
      />
    );
  }
  const link = deepLink.link;
  const flagsOpen = flagsOpenOn !== null && flagsOpenOn === (link.source ?? '');
  const setFlagsOpen = (open: boolean) => setFlagsOpenOn(open ? (link.source ?? '') : null);
  const error = summary.error ?? sources.error;
  if (error !== null) {
    return (
      <ErrorState
        testId={refsTestIds.loadError}
        title="References could not be loaded"
        detail={error.message}
      />
    );
  }
  if (!summary.data || !sources.data) return null;

  const rows = searchSources(filterSources(sources.data, filter), query);
  const citations = sources.data.reduce((total, source) => total + source.uses, 0);
  const quoted = sources.data.reduce((total, source) => total + source.quotedWords, 0);
  const showPanel = view === 'sources' && (flagsOpen || link.source !== undefined);
  return (
    <div data-testid={refsTestIds.page} className="flex h-full min-h-0 flex-col">
      <SlotFill slot="scope">
        <span
          data-testid={refsTestIds.stats}
          className="text-xs whitespace-nowrap text-muted tabular-nums max-[1000px]:hidden"
        >
          {sources.data.length} {sources.data.length === 1 ? 'source' : 'sources'} · {citations}{' '}
          {citations === 1 ? 'citation' : 'citations'} · {quoted} words quoted
        </span>
      </SlotFill>
      <div className="flex shrink-0 flex-wrap items-center gap-x-2 gap-y-1.5 border-b border-border px-3 py-2">
        <h1 className="m-0 mr-2 ml-0.5 text-lg leading-none font-semibold whitespace-nowrap">
          References
        </h1>
        {canWrite && <AddSourceControls bookId={bookId} />}
        <span className="flex-1" />
        {view === 'sources' && (
          <>
            <label className="flex h-[var(--control-height)] w-[220px] max-w-full items-center gap-1.5 rounded-sm border border-border bg-surface px-2 focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent">
              <RefIcon name="search" className="size-[13px] text-muted" />
              <input
                type="search"
                value={query}
                aria-label="Search sources"
                placeholder="Search sources…"
                data-testid={refsTestIds.search}
                onChange={(event) => setQuery(event.target.value)}
                className="min-w-0 flex-1 border-0 bg-transparent text-sm text-text outline-0 placeholder:text-muted"
              />
            </label>
            <label className="relative inline-flex h-[var(--control-height)] cursor-pointer items-center gap-1 rounded-sm border border-border bg-surface pr-2 pl-2 text-sm whitespace-nowrap">
              <span className="text-muted">Group</span>
              <select
                aria-label="Group by"
                value={grouping}
                data-testid={refsTestIds.groupSelect}
                onChange={(event) => setGrouping(event.target.value as Grouping)}
                className="cursor-pointer appearance-none border-0 bg-transparent pr-3.5 text-sm text-text outline-0 field-sizing-content"
              >
                {groupings.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
              <RefIcon
                name="chevron"
                className="pointer-events-none absolute right-2 size-[11px] text-muted"
              />
            </label>
          </>
        )}
        <ViewToggle value={view} onChange={setView} />
      </div>
      <SummaryStrip summary={summary.data} filter={filter} onFilter={setFilter}>
        {canWrite && (
          <QuoteFlagsButton
            bookId={bookId}
            open={flagsOpen}
            onToggle={() => setFlagsOpen(!flagsOpen)}
          />
        )}
      </SummaryStrip>
      {canWrite && returning && link.isNew !== true && (
        <ReturnBar
          returning={returning}
          source={sources.data.find((source) => source.id === link.source)}
        />
      )}
      {view === 'sources' ? (
        <div
          className={`grid min-h-0 flex-1 ${showPanel ? 'grid-cols-[minmax(0,1fr)_var(--panel-width)]' : 'grid-cols-1'}`}
        >
          <div className="min-h-0 overflow-auto">
            <SourceTable
              sources={rows}
              grouping={grouping}
              selectedId={flagsOpen ? undefined : link.source}
              filtered={filter !== 'all' || query !== ''}
              onClearFilters={() => {
                setFilter('all');
                setQuery('');
              }}
              onSelect={(sourceId) => {
                setFlagsOpen(false);
                void navigate(
                  referencesLink(bookId, {
                    source: sourceId,
                    returnTo: link.returnTo,
                    sel: link.sel,
                  }),
                  { replace: true },
                );
              }}
            />
          </div>
          {showPanel && (
            <div className="min-h-0 overflow-auto border-l border-border bg-surface">
              {flagsOpen ? (
                <QuoteFlagsInbox bookId={bookId} onClose={() => setFlagsOpen(false)} />
              ) : (
                link.source !== undefined && (
                  <SourcePanel
                    key={link.source}
                    bookId={bookId}
                    sourceId={link.source}
                    onClose={() =>
                      void navigate(
                        referencesLink(bookId, { returnTo: link.returnTo, sel: link.sel }),
                        {
                          replace: true,
                        },
                      )
                    }
                  />
                )
              )}
            </div>
          )}
        </div>
      ) : (
        <div className="min-h-0 flex-1 overflow-auto">
          <BibliographyView bookId={bookId} />
        </div>
      )}
    </div>
  );
}
