import { Link } from 'react-router-dom';
import {
  groupOptions,
  sortOptions,
  statusOptions,
  type LibraryGroup,
  type LibrarySort,
  type LibraryStatus,
} from '@codex/core/api/library';
import { newBookLink } from '@codex/core/shell/links';
import { testIds } from '../../testIds';
import { ImportIcon, PlusIcon } from './icons';
import { importLink, newSeriesLink } from './libraryLinks';
import type { LibraryViewState } from './libraryView';
import { SearchBox } from './SearchBox';
import { libraryTestIds } from './testIds';
import { ToolbarSelect } from './ToolbarSelect';
import { ViewToggle } from './ViewToggle';
import { FilterToggle } from './FilterToggle';
import type { LibraryFilter } from '@codex/core/features/library/sharedBooks';

const sortLabels: Record<LibrarySort, string> = {
  lastEdited: 'Last edited',
  title: 'Title',
  series: 'Series',
  progress: 'Progress',
};
const groupLabels: Record<LibraryGroup, string> = {
  series: 'Series',
  status: 'Status',
  none: 'None',
};
const sortChoices = sortOptions.map((value) => ({ value, label: sortLabels[value] }));
const groupChoices = groupOptions.map((value) => ({ value, label: groupLabels[value] }));
const statusChoices = [
  { value: '', label: 'Any' },
  ...statusOptions.map((value) => ({
    value,
    label: value.charAt(0).toUpperCase() + value.slice(1),
  })),
];

const buttonClass =
  'inline-flex h-[var(--control-height)] items-center gap-[5px] rounded-sm border px-[10px] text-sm font-medium whitespace-nowrap no-underline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&>svg]:size-[13px]';

// design/screens/library.html toolbar, one 44px row: Create Novel, Import, spacer, then search, Sort, Group, Status
// and the Grid/List control. Everything lives in the URL (All / Mine / Shared with me in the hash, as the mockup
// has it). The Status filter is not in the mockup yet.
export function LibraryToolbar({
  state,
  filter,
}: {
  state: LibraryViewState;
  /** All / Mine / Shared with me: the Library's, not the Series screen's (#80). */
  filter?: { value: LibraryFilter; onChange: (filter: LibraryFilter) => void };
}) {
  const { view, update } = state;
  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5 border-b border-border px-3 py-2">
      <Link
        to={newBookLink()}
        data-testid={libraryTestIds.newBook}
        className={`${buttonClass} border-accent bg-accent text-accent-fg hover:brightness-[1.07]`}
      >
        <PlusIcon />
        Create Novel
      </Link>
      <Link
        to={importLink()}
        data-testid={libraryTestIds.import}
        className={`${buttonClass} border-border bg-surface text-text hover:bg-surface-2`}
      >
        <ImportIcon />
        Import
      </Link>
      <Link
        to={newSeriesLink()}
        data-testid={libraryTestIds.newSeries}
        className={`${buttonClass} border-border bg-surface text-text hover:bg-surface-2`}
      >
        <PlusIcon />
        New series
      </Link>
      <span className="flex-1" />
      <SearchBox value={view.q} onChange={(q) => update({ q }, { replace: true })} />
      {filter && <FilterToggle value={filter.value} onChange={filter.onChange} />}
      <ToolbarSelect
        label="Sort"
        testId={testIds.shell.sortControl}
        value={view.sort}
        options={sortChoices}
        onChange={(value) => {
          const sort = sortOptions.find((option) => option === value);
          if (sort) update({ sort });
        }}
      />
      <ToolbarSelect
        label="Group"
        testId={testIds.shell.groupControl}
        value={view.group}
        options={groupChoices}
        onChange={(value) => {
          const group = groupOptions.find((option) => option === value);
          if (group) update({ group });
        }}
      />
      <ToolbarSelect
        label="Status"
        testId={libraryTestIds.statusFilter}
        value={view.status ?? ''}
        options={statusChoices}
        onChange={(value) => {
          const status: LibraryStatus | null =
            statusOptions.find((option) => option === value) ?? null;
          update({ status });
        }}
      />
      <ViewToggle value={view.view} onChange={(mode) => update({ view: mode })} />
    </div>
  );
}
