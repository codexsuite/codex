import { libraryFilters, type LibraryFilter } from '@codex/core/features/library/sharedBooks';
import { libraryTestIds } from './testIds';

const labels: Record<LibraryFilter, string> = {
  all: 'All',
  mine: 'Mine',
  shared: 'Shared with me',
};

// All / Mine / Shared with me (#80): a segmented control before Sort, in the ViewToggle's style.
export function FilterToggle({
  value,
  onChange,
}: {
  value: LibraryFilter;
  onChange: (filter: LibraryFilter) => void;
}) {
  return (
    <div
      role="group"
      aria-label="Show"
      data-testid={libraryTestIds.filter}
      className="inline-flex h-[var(--control-height)] max-w-full overflow-hidden rounded-sm border border-border bg-surface"
    >
      {libraryFilters.map((filter) => (
        <button
          key={filter}
          type="button"
          data-testid={libraryTestIds.filterOption(filter)}
          aria-pressed={value === filter}
          onClick={() => onChange(filter)}
          className="inline-flex cursor-pointer items-center border-0 border-r border-border bg-transparent px-[9px] text-sm whitespace-nowrap text-muted last:border-r-0 focus-visible:outline-2 focus-visible:outline-accent aria-pressed:bg-accent-soft aria-pressed:font-semibold aria-pressed:text-text"
        >
          {labels[filter]}
        </button>
      ))}
    </div>
  );
}
