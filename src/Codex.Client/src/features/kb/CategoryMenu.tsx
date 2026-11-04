import { useState } from 'react';
import type { KbCategory } from '@codex/core/api/kb';
import { ChevronDownIcon } from './icons';
import { categoryTint } from './kbStyles';
import { NewCategoryForm } from './NewCategoryForm';
import { kbTestIds } from './testIds';
import { useCanWrite } from '../../shell/bookAccess';

interface CategoryMenuProps {
  categories: KbCategory[];
  currentName: string;
  colour: string | null;
  onPick: (categoryId: string) => Promise<unknown>;
  onCreate: (name: string, colour: string) => Promise<unknown>;
}

// The category chip in the header: change the entry's category, or create a new one with a colour.
export function CategoryMenu({
  categories,
  currentName,
  colour,
  onPick,
  onCreate,
}: CategoryMenuProps) {
  const [open, setOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  // Below write (#80) the chip names the category but does not open.
  const canWrite = useCanWrite();

  return (
    <div className="relative">
      <button
        type="button"
        data-testid={kbTestIds.categoryChip}
        aria-expanded={open}
        disabled={!canWrite}
        onClick={() => setOpen(!open)}
        style={{ ['--cat' as string]: colour ? `var(--${colour})` : 'currentColor' }}
        className={`inline-flex h-6 items-center gap-1.5 rounded-sm border-0 pr-[7px] pl-2 text-xs font-semibold whitespace-nowrap text-text ${categoryTint}`}
      >
        <i className="h-[7px] w-[7px] rounded-[2px] bg-[var(--cat)]" />
        {currentName}
        {canWrite && (
          <span className="text-muted">
            <ChevronDownIcon size={10} />
          </span>
        )}
      </button>
      {open && (
        <div className="absolute left-0 z-30 mt-1 w-56 rounded border border-border bg-surface shadow-lg">
          {categories.map((category) => (
            <button
              key={category.id}
              type="button"
              data-testid={kbTestIds.categoryOption(category.id)}
              onClick={() => {
                void onPick(category.id).then(() => setOpen(false));
              }}
              className="flex h-[var(--row-height)] w-full items-center gap-2 border-0 bg-transparent px-3 text-left text-sm text-text hover:bg-accent-soft"
            >
              <i
                className="h-[7px] w-[7px] rounded-[2px]"
                style={{
                  background: category.colour ? `var(--${category.colour})` : 'currentColor',
                }}
              />
              {category.name}
            </button>
          ))}
          {creating ? (
            <NewCategoryForm
              onCreate={async (name, token) => {
                await onCreate(name, token);
                setOpen(false);
                setCreating(false);
              }}
            />
          ) : (
            <button
              type="button"
              onClick={() => setCreating(true)}
              data-testid={kbTestIds.newCategoryOpen}
              className="h-[var(--row-height)] w-full border-0 border-t border-border bg-transparent px-3 text-left text-sm text-accent"
            >
              New category…
            </button>
          )}
        </div>
      )}
    </div>
  );
}
