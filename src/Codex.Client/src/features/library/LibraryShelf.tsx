import type { Library } from '@codex/core/api/types';
import type { LibraryGroup } from '@codex/core/api/library';
import { newBookLink } from '@codex/core/shell/links';
import { testIds } from '../../testIds';
import { AddTile } from './AddTile';
import { BookTile } from './BookTile';
import { SeriesStack } from './SeriesStack';
import { libraryTestIds } from './testIds';

const shelfClass = 'flex flex-wrap content-start gap-[var(--shelf-gap)]';

function GroupHeading({ title, count }: { title: string; count: string }) {
  return (
    <div className="mt-0.5 mb-2.5 flex items-baseline gap-2">
      <h2 className="m-0 text-md font-semibold text-text">{title}</h2>
      <span className="text-xs text-muted">{count}</span>
    </div>
  );
}

// The grid: series stacks first, then standalone books and the + New book tile (left out while a search or status
// filter is active: the shelf is then a result list). Headings, and a rule between the groups, only when grouped by
// series.
export function LibraryShelf({
  library,
  group,
  filtered,
}: {
  library: Library;
  group: LibraryGroup;
  filtered: boolean;
}) {
  const headed = group === 'series';
  const bookCount = library.series.reduce((total, series) => total + series.bookCount, 0);
  const ruled = headed && library.series.length > 0;
  return (
    <main data-testid={testIds.shell.shelf} className="px-3 pt-1 pb-4">
      <section data-testid={libraryTestIds.stackGroup} className="pt-2.5 pb-1">
        {headed && library.series.length > 0 && (
          <GroupHeading
            title="Series"
            count={`${library.series.length} series · ${bookCount} books`}
          />
        )}
        <div className={shelfClass}>
          {library.series.map((series) => (
            <SeriesStack key={series.id} series={series} />
          ))}
        </div>
      </section>
      <section
        data-testid={libraryTestIds.standaloneGroup}
        className={`pt-2.5 pb-1 ${ruled ? 'border-t border-border' : ''}`}
      >
        {headed && library.standalone.length > 0 && (
          <GroupHeading title="Standalone" count={`${library.standalone.length} books`} />
        )}
        <div className={shelfClass}>
          {library.standalone.map((book) => (
            <BookTile key={book.id} book={book} />
          ))}
          {!filtered && (
            <AddTile to={newBookLink()} label="+ New book" testId={libraryTestIds.addTile} />
          )}
        </div>
      </section>
    </main>
  );
}
