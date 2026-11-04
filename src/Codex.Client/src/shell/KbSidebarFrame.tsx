import type { ReactNode } from 'react';
import type {
  KbSidebarCategory,
  KbSidebarGroup,
  KbSidebarSection,
} from '@codex/core/features/kb/kbSidebar';
import { PlusIcon, SearchIcon } from '../features/kb/icons';
import { iconButton } from '../features/kb/kbStyles';
import { testIds } from '../testIds';
import { kbTestIds } from '../features/kb/testIds';
import { KbCategoryFilter } from './KbCategoryFilter';
import { SeriesIcon } from './SeriesIcon';
import { setEntryDragData } from './dragPayload';
import { shellTestIds } from './shellTestIds';

interface KbSidebarFrameProps {
  sections: KbSidebarSection[];
  /** Entries the book has before the search filter; 0 shows the empty state. */
  totalEntries: number;
  status?: 'loading' | 'ready' | 'error';
  errorMessage?: string;
  query?: string;
  onQueryChange?: (query: string) => void;
  /** Entry id highlighted right now. */
  highlighted?: string | null;
  onSelect?: (entryId: string) => void;
  /** The `+ New Entry` button (the first category) and the `+` on a category heading: start a new entry in that category. */
  onNewEntry?: (categoryId: string) => void;
  /** Every category, for the filter beside the search; `sections` already leaves out the hidden ones. */
  categories?: KbSidebarCategory[];
  hiddenCategories?: readonly string[];
  onHiddenCategoriesChange?: (hidden: string[]) => void;
  /** A menu beside the `+` on the heading of one of the book's own categories (not series ones): rename, colour, delete. */
  renderCategoryMenu?: (group: KbSidebarGroup) => ReactNode;
  /** Below write (#80): no New Entry, no category menus, and entries do not drag onto scenes. */
  readOnly?: boolean;
}

// The Knowledge base sidebar: search with the category filter, categories (own and `From <series>`), entries with scene counts, empty and
// error states. Each entry is a drag source carrying the `application/x-codex-entry` payload.
export function KbSidebarFrame({
  sections,
  totalEntries,
  status = 'ready',
  errorMessage,
  query = '',
  onQueryChange,
  highlighted = null,
  onSelect,
  onNewEntry,
  categories = [],
  hiddenCategories = [],
  onHiddenCategoriesChange,
  renderCategoryMenu,
  readOnly = false,
}: KbSidebarFrameProps) {
  const showBookHeading = sections.length > 1;
  // `+ New Entry` starts in the book's own first category, else the first one listed.
  const firstGroup = (sections.find((section) => section.key === 'book') ?? sections[0])?.groups[0];
  return (
    <aside
      data-testid={testIds.shell.kbSidebar}
      data-shell-region="kb-sidebar"
      aria-label="Knowledge base"
      className="flex w-[var(--sidebar-width)] shrink-0 flex-col border-r border-border bg-surface"
    >
      <h2 className="m-0 px-3 pt-2.5 text-md font-semibold">Knowledge base</h2>
      <div className="flex gap-1.5 p-2">
        <label className="flex h-[var(--control-height)] min-w-0 flex-1 items-center gap-1.5 rounded-sm border border-border bg-surface px-2 focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent">
          <span className="text-muted">
            <SearchIcon />
          </span>
          <input
            type="search"
            data-testid={kbTestIds.search}
            aria-label="Search Knowledge base"
            placeholder="Search Knowledge base"
            value={query}
            onChange={(event) => onQueryChange?.(event.target.value)}
            className="min-w-0 flex-1 border-0 bg-transparent text-sm text-text outline-none placeholder:text-muted"
          />
        </label>
        <KbCategoryFilter
          categories={categories}
          hidden={hiddenCategories}
          onChange={(hidden) => onHiddenCategoriesChange?.(hidden)}
        />
      </div>
      {!readOnly && (
        <div className="px-2 pb-2">
          <button
            type="button"
            data-testid={kbTestIds.newEntryButton}
            disabled={!firstGroup}
            onClick={() => firstGroup && onNewEntry?.(firstGroup.id)}
            className="inline-flex h-[var(--control-height)] w-full items-center justify-center gap-1.5 rounded-sm border border-border bg-surface text-sm font-medium text-text hover:bg-surface-2 disabled:opacity-50 disabled:hover:bg-surface"
          >
            <PlusIcon />
            New Entry
          </button>
        </div>
      )}
      <div data-testid={testIds.shell.kbList} className="min-h-0 flex-1 overflow-auto px-1 pb-3">
        {sections.map((section) => (
          <section key={section.key} data-testid={kbTestIds.section(section.key)}>
            {section.key === 'series' ? (
              <h3 className="m-0 mx-1.5 flex items-center gap-1.5 pt-2 text-2xs font-medium text-muted">
                <SeriesIcon />
                <span>
                  From <b className="font-semibold text-text">{section.seriesName}</b>
                </span>
              </h3>
            ) : (
              showBookHeading && (
                <h3 className="m-0 mx-1.5 mt-2 flex items-center gap-1.5 border-t border-border pt-1.5 text-2xs font-medium text-muted">
                  This book
                </h3>
              )
            )}
            {section.groups.map((group) => (
              <div
                key={group.id}
                data-testid={kbTestIds.category(group.id)}
                style={{
                  ['--cat' as string]: group.colour ? `var(--${group.colour})` : 'currentColor',
                }}
              >
                <h4 className="group m-0 flex items-center gap-1.5 pt-2.5 pr-1.5 pb-1 pl-1.5 text-2xs font-semibold tracking-[.04em] text-muted uppercase">
                  <i className="h-[7px] w-[7px] shrink-0 rounded-[2px] bg-[var(--cat)]" />
                  <span className="flex-1">{group.name}</span>
                  {!readOnly && (
                    <button
                      type="button"
                      data-testid={kbTestIds.newEntry(group.id)}
                      aria-label={`New entry in ${group.name}`}
                      title={`New entry in ${group.name}`}
                      onClick={() => onNewEntry?.(group.id)}
                      className={`${iconButton} -my-1 h-[18px] w-[18px] [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-focus-within:opacity-100 [@media(hover:hover)]:group-hover:opacity-100`}
                    >
                      <PlusIcon size={11} />
                    </button>
                  )}
                  {!readOnly && section.key === 'book' && renderCategoryMenu?.(group)}
                  <span>{group.entries.length}</span>
                </h4>
                <ul className="m-0 list-none p-0">
                  {group.entries.map((entry) => (
                    <li key={entry.id}>
                      <button
                        type="button"
                        draggable={!readOnly}
                        data-testid={testIds.shell.kbItem(entry.id)}
                        aria-current={highlighted === entry.id}
                        onClick={() => onSelect?.(entry.id)}
                        onDragStart={(event) =>
                          setEntryDragData(event.dataTransfer, {
                            entryId: entry.id,
                            name: entry.name,
                          })
                        }
                        className="flex h-7 w-full items-center gap-[7px] rounded-sm border-0 bg-transparent px-1.5 text-left text-sm text-text hover:bg-surface-2 aria-[current=true]:bg-accent-soft aria-[current=true]:font-medium"
                      >
                        <i className="h-[7px] w-[7px] shrink-0 rounded-[2px] bg-[var(--cat)]" />
                        <span className="min-w-0 flex-1 truncate">{entry.name}</span>
                        <span
                          data-testid={kbTestIds.count(entry.id)}
                          className="text-2xs text-muted tabular-nums"
                        >
                          {entry.sceneCount}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </section>
        ))}
      </div>
      {status === 'error' && (
        <p data-testid={kbTestIds.error} role="alert" className="m-0 px-3 py-2 text-xs text-danger">
          {errorMessage ?? 'The Knowledge base could not be loaded.'}
        </p>
      )}
      {status === 'ready' && totalEntries === 0 && (
        <p data-testid={shellTestIds.kbEmpty} className="m-0 px-3 py-2 text-xs text-muted">
          No entries yet.
        </p>
      )}
      {status === 'ready' && totalEntries > 0 && sections.length === 0 && (
        <p data-testid={kbTestIds.noMatch} className="m-0 px-3 py-2 text-xs text-muted">
          No entries match.
        </p>
      )}
    </aside>
  );
}
