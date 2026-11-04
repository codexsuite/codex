import type { UseQueryResult } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { Library } from '@codex/core/api/types';
import { SeriesStats } from './SeriesStats';
import { StackCovers } from '../library/SeriesStack';
import { AddTile } from '../library/AddTile';
import { BookTile } from '../library/BookTile';
import { LibraryAppBar } from '../library/LibraryAppBar';
import { LibraryList } from '../library/LibraryList';
import { LibraryToolbar } from '../library/LibraryToolbar';
import type { LibraryViewState } from '../library/libraryView';
import { newBookInSeriesLink } from '../library/libraryLinks';
import { libraryTestIds } from '../library/testIds';
import { libraryLink } from '@codex/core/shell/links';
import { ErrorState } from '../../shell/ErrorState';
import { testIds } from '../../testIds';
import { DeleteSeriesDialog } from './DeleteSeriesDialog';
import { SeriesSettingsDialog } from './SeriesSettingsDialog';
import { seriesTestIds } from './testIds';
import { seriesShare } from '@codex/core/features/library/sharedBooks';
import { PersonIcon, RoleChip } from '../library/SharedMarks';

// The Series screen (`/#series=<id>`): the Library's app bar and toolbar, the series header, then the series' books. Back returns to the Library.
export function SeriesPage({
  seriesId,
  library,
  state,
}: {
  seriesId: string;
  library: UseQueryResult<Library>;
  /** The Library's toolbar state (URL query); the Grid/List control applies here too. */
  state: LibraryViewState;
}) {
  const series = library.data?.series.find((s) => s.id === seriesId);
  // A series someone else shared with you (#80): who, your role, and no Series settings or Add book.
  const share = series ? seriesShare(series) : null;
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const opener = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);
  // Focus goes back to the opener once the modal is gone (a modal dialog keeps the page inert until then).
  useEffect(() => {
    if (wasOpen.current && !settingsOpen && !deleteOpen) opener.current?.focus();
    wasOpen.current = settingsOpen || deleteOpen;
  }, [settingsOpen, deleteOpen]);
  return (
    <div data-testid={testIds.shell.seriesScreen} className="flex min-h-screen w-full flex-col">
      <LibraryAppBar
        crumbs={
          <nav
            aria-label="Breadcrumb"
            data-testid={seriesTestIds.breadcrumb}
            className="flex min-w-0 flex-1 items-center gap-1.5 overflow-hidden text-sm whitespace-nowrap text-muted"
          >
            <Link
              to={libraryLink()}
              data-testid={testIds.shell.backLink}
              className="text-inherit no-underline hover:text-text"
            >
              Library
            </Link>
            {series && (
              <>
                <span aria-hidden="true">/</span>
                <b
                  aria-current="page"
                  className="overflow-hidden font-medium text-ellipsis text-text"
                >
                  {series.name}
                </b>
              </>
            )}
          </nav>
        }
      />
      <LibraryToolbar state={state} />
      {library.isError && (
        <ErrorState
          testId={libraryTestIds.loadError}
          title="The series could not be loaded"
          detail={library.error.message}
        />
      )}
      {library.data && !series && (
        <ErrorState
          testId={testIds.shell.errorUnknownBook}
          title="That series doesn't exist"
          detail={`No series ${seriesId}.`}
        />
      )}
      {series && (
        <>
          <section className="mx-3 mt-1 mb-3 flex flex-wrap items-start gap-4 border-b border-border pt-3 pb-3.5">
            <div style={{ ['--cover-width' as string]: '64px' }}>
              <StackCovers series={series} showCount={false} />
            </div>
            <div className="min-w-[200px] flex-1">
              <h1
                data-testid={seriesTestIds.title}
                className="m-0 mb-[3px] text-lg font-semibold text-text"
              >
                {series.name}
              </h1>
              {series.description && (
                <p className="m-0 mb-2 max-w-[60ch] text-sm text-muted">{series.description}</p>
              )}
              <SeriesStats series={series} />
              {share && (
                <div
                  data-testid={libraryTestIds.seriesShared}
                  className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-muted"
                >
                  <PersonIcon className="size-[13px]" />
                  <span>
                    Shared by <b className="font-medium text-text">{share.owner}</b>
                  </span>
                  <RoleChip role={share.role} />
                </div>
              )}
            </div>
            {!share && (
              <div className="flex items-start gap-1.5">
                <button
                  type="button"
                  ref={opener}
                  data-testid={seriesTestIds.settingsButton}
                  onClick={() => setSettingsOpen(true)}
                  className="h-[var(--control-height)] cursor-pointer rounded-sm border border-border bg-surface px-[10px] text-sm font-medium text-text hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent"
                >
                  Series settings
                </button>
                <Link
                  to={newBookInSeriesLink(series.id)}
                  data-testid={seriesTestIds.addBook}
                  className="inline-flex h-[var(--control-height)] items-center rounded-sm border border-accent bg-accent px-[10px] text-sm font-medium text-accent-fg no-underline hover:brightness-[1.07] focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent"
                >
                  + Add book
                </Link>
              </div>
            )}
          </section>
          {share && series.partlyShared && (
            <p
              data-testid={libraryTestIds.partlySharedNote}
              className="mx-3 mt-0 mb-3 rounded-md border border-[color-mix(in_srgb,var(--color-info)_35%,var(--color-border))] bg-[color-mix(in_srgb,var(--color-info)_7%,var(--color-surface))] px-2.5 py-2 text-xs text-text"
            >
              {share.owner} shared {series.books.length === 1 ? 'one book' : 'some books'} in this
              series with you. Only{' '}
              {series.books.length === 1 ? 'that book appears' : 'those books appear'} here.
            </p>
          )}
          {state.view.view === 'list' ? (
            <LibraryList
              library={{ series: [series], standalone: [] }}
              group="none"
              sort={state.view.sort}
            />
          ) : (
            <main className="flex flex-wrap content-start gap-[var(--shelf-gap)] px-3 pb-4">
              {series.books.map((book) => (
                <BookTile key={book.id} book={book} inSeries />
              ))}
              {!share && (
                <AddTile
                  to={newBookInSeriesLink(series.id)}
                  label="+ Add book"
                  testId={seriesTestIds.addTile}
                />
              )}
            </main>
          )}
          {settingsOpen && !share && (
            <SeriesSettingsDialog
              series={series}
              onClose={() => setSettingsOpen(false)}
              onDelete={() => {
                setSettingsOpen(false);
                setDeleteOpen(true);
              }}
            />
          )}
          {deleteOpen && (
            <DeleteSeriesDialog series={series} onClose={() => setDeleteOpen(false)} />
          )}
        </>
      )}
    </div>
  );
}
