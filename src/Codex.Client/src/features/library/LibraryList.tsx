import { Fragment } from 'react';
import type { Library } from '@codex/core/api/types';
import type { LibraryGroup, LibrarySort } from '@codex/core/api/library';
import { testIds } from '../../testIds';
import { BookRow } from './BookRow';
import { listGroups } from '@codex/core/features/library/listGroups';
import { libraryTestIds } from './testIds';

const head = 'px-2 py-1 text-left text-2xs font-medium tracking-[.02em] text-muted uppercase';

// The dense table, with a group row per series / status and a trailing Standalone group.
export function LibraryList({
  library,
  group,
  sort,
}: {
  library: Library;
  group: LibraryGroup;
  sort: LibrarySort;
}) {
  const archived =
    library.standalone.some((b) => b.archived) ||
    library.series.some((s) => s.books.some((b) => b.archived));
  return (
    <main data-testid={testIds.shell.listView} className="overflow-x-auto px-3 pt-1 pb-4">
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr className="border-b border-border">
            <th className={head}>Title</th>
            <th className={head}>Owner</th>
            <th className={head}>Series</th>
            <th className={`${head} text-right`}>#</th>
            <th className={`${head} text-right`}>Words</th>
            <th className={head}>Progress</th>
            <th className={head}>Status</th>
            <th className={head}>Edited</th>
            {archived && <th className={head} />}
          </tr>
        </thead>
        <tbody>
          {listGroups(library, group, sort).map((entry) => (
            <Fragment key={entry.key}>
              {entry.label !== null && (
                <tr data-testid={libraryTestIds.groupRow} className="bg-surface-2">
                  <td
                    colSpan={archived ? 9 : 8}
                    className="px-2 py-1 text-xs font-semibold text-text"
                  >
                    {entry.label} · {entry.books.length}
                    {entry.sharedBy && ` · Shared by ${entry.sharedBy}`}
                  </td>
                </tr>
              )}
              {entry.books.map((book) => (
                <BookRow key={book.id} book={book} />
              ))}
            </Fragment>
          ))}
        </tbody>
      </table>
    </main>
  );
}
