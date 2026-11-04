import type { ReactNode } from 'react';
import { testIds } from '../../testIds';
import { GridIcon, ListIcon } from './icons';
import { libraryTestIds } from './testIds';
import type { LibraryViewMode } from './libraryView';

const modes: { mode: LibraryViewMode; label: string; testId: string; icon: ReactNode }[] = [
  { mode: 'grid', label: 'Grid', testId: libraryTestIds.viewGrid, icon: <GridIcon /> },
  { mode: 'list', label: 'List', testId: libraryTestIds.viewList, icon: <ListIcon /> },
];

// Grid / List segmented control (design/screens/library.html `.seg`): icon buttons, the pressed one on accent-soft.
export function ViewToggle({
  value,
  onChange,
}: {
  value: LibraryViewMode;
  onChange: (mode: LibraryViewMode) => void;
}) {
  return (
    <div
      role="group"
      aria-label="View as"
      data-testid={testIds.shell.viewToggle}
      className="inline-flex h-[var(--control-height)] overflow-hidden rounded-sm border border-border bg-surface"
    >
      {modes.map(({ mode, label, testId, icon }) => (
        <button
          key={mode}
          type="button"
          data-testid={testId}
          aria-label={label}
          title={label}
          aria-pressed={value === mode}
          onClick={() => onChange(mode)}
          className="inline-flex cursor-pointer items-center gap-1 border-0 border-r border-border bg-transparent px-[9px] text-sm text-muted last:border-r-0 focus-visible:outline-2 focus-visible:outline-accent aria-pressed:bg-accent-soft aria-pressed:font-semibold aria-pressed:text-text [&>svg]:size-[13px]"
        >
          {icon}
        </button>
      ))}
    </div>
  );
}
