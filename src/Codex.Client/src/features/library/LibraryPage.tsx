import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { defaultLibraryParams } from '@codex/core/api/library';
import { useLibrary } from '../../api/libraryQueries';
import { ErrorState } from '../../shell/ErrorState';
import { useDeepLink } from '../../shell/useDeepLink';
import { testIds } from '../../testIds';
import { SeriesPage } from '../series/SeriesPage';
import { ArchivedSeriesList } from './ArchivedSeriesList';
import { ImportDialog } from './ImportDialog';
import { LibraryAppBar } from './LibraryAppBar';
import { LibraryList } from './LibraryList';
import { LibraryShelf } from './LibraryShelf';
import { LibraryToolbar } from './LibraryToolbar';
import { useLibraryView } from './libraryView';
import { NewBookDialog } from './NewBookDialog';
import { NewSeriesDialog } from './NewSeriesDialog';
import { NoResults } from './NoResults';
import { libraryTestIds } from './testIds';
import {
  filterLibrary,
  readFilter,
  withFilter,
  type LibraryFilter,
} from '@codex/core/features/library/sharedBooks';

// The Library: toolbar over a shelf (stacks, then standalone) or a list. `#series=<id>` shows that series instead.
// `?book=new` opens the New book dialog (over the Library or a series), `?import=1` the Import dialog.
export function LibraryPage() {
  const deepLink = useDeepLink();
  const navigate = useNavigate();
  const { hash, search } = useLocation();
  const [searchParams] = useSearchParams();
  const closeDialog = () => void navigate({ pathname: '/', hash }, { replace: true });
  const state = useLibraryView();
  const { view, update } = state;
  const inSeries = deepLink.ok && deepLink.link.series !== undefined;
  const library = useLibrary(inSeries ? defaultLibraryParams : view);
  const filter = readFilter(hash);
  const setFilter = (next: LibraryFilter) =>
    void navigate({ search, hash: withFilter(hash, next) }, { replace: true });

  if (!deepLink.ok) {
    return (
      <ErrorState
        testId={testIds.shell.errorBadLink}
        title="This link doesn't make sense"
        detail={deepLink.error}
      />
    );
  }
  const dialogs = (
    <>
      {deepLink.link.newBook && (
        <NewBookDialog seriesId={deepLink.link.series} onClose={closeDialog} />
      )}
      {searchParams.get('import') === '1' && <ImportDialog onClose={closeDialog} />}
      {searchParams.get('newSeries') === '1' && <NewSeriesDialog onClose={closeDialog} />}
    </>
  );
  if (deepLink.link.series !== undefined) {
    return (
      <>
        <SeriesPage seriesId={deepLink.link.series} library={library} state={state} />
        {dialogs}
      </>
    );
  }

  // The Archived list is flat: an archived book is shown with its Restore control, not inside its series' stack.
  // A series that is itself archived is one row above it (its books come back with it, so they get no Restore).
  // All / Mine / Shared with me narrows what came back (#80).
  const shown = library.data && filterLibrary(library.data, filter);
  const archivedView = view.status === 'archived' && shown !== undefined;
  const archivedSeries = archivedView ? shown.series.filter((s) => s.archived) : [];
  const data =
    archivedView && shown
      ? {
          series: [],
          standalone: [
            ...shown.series.filter((s) => !s.archived).flatMap((s) => s.books),
            ...shown.standalone,
          ],
        }
      : shown;
  const hasShelf = data !== undefined && data.series.length + data.standalone.length > 0;
  const empty = data !== undefined && !hasShelf && archivedSeries.length === 0;
  const nothingShared = empty && filter === 'shared' && view.q === '' && view.status === null;
  return (
    <>
      <div data-testid={testIds.shell.libraryPage} className="flex min-h-screen w-full flex-col">
        <LibraryAppBar
          crumbs={
            <nav aria-label="Breadcrumb" className="min-w-0 flex-1 overflow-hidden text-sm">
              <h1 className="m-0 text-sm font-medium text-text">Library</h1>
            </nav>
          }
        />
        <LibraryToolbar state={state} filter={{ value: filter, onChange: setFilter }} />
        {library.isError && (
          <ErrorState
            testId={libraryTestIds.loadError}
            title="The Library could not be loaded"
            detail={library.error.message}
            action={
              <button
                type="button"
                onClick={() => void library.refetch()}
                className="cursor-pointer rounded-sm border border-border bg-surface px-2 py-1 text-sm text-accent"
              >
                Try again
              </button>
            }
          />
        )}
        {archivedSeries.length > 0 && <ArchivedSeriesList series={archivedSeries} />}
        {nothingShared && (
          <div
            data-testid={libraryTestIds.sharedEmpty}
            className="max-w-[52ch] px-3 py-4 text-sm text-muted"
          >
            <b className="mb-0.5 block font-medium text-text">Nothing is shared with you yet.</b>
            When someone shares a book or a series with you, it shows up here.
          </div>
        )}
        {empty && !nothingShared && <NoResults q={view.q} onClear={() => update({ q: '' })} />}
        {data && hasShelf && view.view === 'list' && (
          <LibraryList library={data} group={view.group} sort={view.sort} />
        )}
        {data && hasShelf && view.view === 'grid' && (
          <LibraryShelf
            library={data}
            group={view.group}
            filtered={view.q !== '' || view.status !== null || filter === 'shared'}
          />
        )}
      </div>
      {dialogs}
    </>
  );
}
