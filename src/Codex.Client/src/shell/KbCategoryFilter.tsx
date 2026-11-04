import { useEffect, useRef, useState } from 'react';
import { FilterIcon } from '../features/kb/icons';
import { kbTestIds } from '../features/kb/testIds';
import type { KbSidebarCategory } from '@codex/core/features/kb/kbSidebar';

interface KbCategoryFilterProps {
  categories: KbSidebarCategory[];
  hidden: readonly string[];
  onChange: (hidden: string[]) => void;
}

// The funnel beside the sidebar search (`.side-tools .ib[aria-label="Filter"]`): a panel of the book's categories
// as checkboxes. An unchecked category is left out of the list below; Show all brings every one back. A click
// outside or Esc closes it.
export function KbCategoryFilter({ categories, hidden, onChange }: KbCategoryFilterProps) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const hiddenCount = categories.filter((c) => hidden.includes(c.id)).length;

  useEffect(() => {
    if (!open) return undefined;
    const dismiss = (event: Event) => {
      if (
        event instanceof KeyboardEvent
          ? event.key === 'Escape'
          : !root.current?.contains(event.target as Node)
      )
        setOpen(false);
    };
    document.addEventListener('pointerdown', dismiss);
    document.addEventListener('keydown', dismiss);
    return () => {
      document.removeEventListener('pointerdown', dismiss);
      document.removeEventListener('keydown', dismiss);
    };
  }, [open]);

  const toggle = (categoryId: string, shown: boolean) =>
    onChange(
      shown ? hidden.filter((id) => id !== categoryId) : [...new Set([...hidden, categoryId])],
    );

  return (
    <div ref={root} className="relative">
      <button
        type="button"
        data-testid={kbTestIds.filter}
        data-active={hiddenCount > 0}
        aria-label={
          hiddenCount > 0
            ? `Filter Knowledge base (${hiddenCount} ${hiddenCount === 1 ? 'category' : 'categories'} hidden)`
            : 'Filter Knowledge base'
        }
        title="Filter by category"
        aria-haspopup="true"
        aria-expanded={open}
        disabled={categories.length === 0}
        onClick={() => setOpen(!open)}
        className="inline-grid size-[var(--control-height)] place-items-center rounded-sm border border-border bg-surface p-0 text-muted hover:bg-surface-2 hover:text-text focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent disabled:opacity-50 data-[active=true]:border-[color-mix(in_srgb,var(--color-accent)_40%,var(--color-border))] data-[active=true]:bg-accent-soft data-[active=true]:text-accent"
      >
        <FilterIcon />
      </button>
      {open && (
        <div
          data-testid={kbTestIds.filterPanel}
          role="group"
          aria-label="Show categories"
          className="absolute right-0 z-20 mt-1 flex max-h-80 w-56 flex-col overflow-hidden rounded-md border border-border bg-surface shadow-raised"
        >
          <div className="flex items-center px-2.5 pt-2 pb-1 text-2xs font-semibold tracking-[.04em] text-muted uppercase">
            <span className="flex-1">Categories</span>
            <button
              type="button"
              data-testid={kbTestIds.filterShowAll}
              disabled={hiddenCount === 0}
              onClick={() => onChange([])}
              className="rounded-xs border-0 bg-transparent px-1 text-2xs font-medium tracking-normal text-accent normal-case hover:underline disabled:text-muted disabled:no-underline"
            >
              Show all
            </button>
          </div>
          <div className="overflow-auto p-1">
            {categories.map((category) => {
              const shown = !hidden.includes(category.id);
              return (
                <label
                  key={category.id}
                  className="flex min-h-7 cursor-pointer items-center gap-[7px] rounded-sm px-2 text-sm text-text hover:bg-surface-2"
                  style={{
                    ['--cat' as string]: category.colour
                      ? `var(--${category.colour})`
                      : 'currentColor',
                  }}
                >
                  <input
                    type="checkbox"
                    data-testid={kbTestIds.filterOption(category.id)}
                    checked={shown}
                    onChange={(event) => toggle(category.id, event.target.checked)}
                    className="m-0 accent-[var(--color-accent)]"
                  />
                  <i className="h-[7px] w-[7px] shrink-0 rounded-[2px] bg-[var(--cat)]" />
                  <span className="min-w-0 flex-1 truncate">{category.name}</span>
                </label>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
