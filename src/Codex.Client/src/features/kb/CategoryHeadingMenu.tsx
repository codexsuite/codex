import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { kbKey } from '@codex/core/api/kb';
import {
  deleteKbCategory,
  getKbCategoryDeleteImpact,
  patchKbCategory,
} from '@codex/core/api/kbEntries';
import { showToast } from '@codex/core/editing/toastStore';
import { categoryColours } from './categoryColours';
import { iconButton } from './kbStyles';
import { kbTestIds } from './testIds';

interface CategoryHeadingMenuProps {
  bookId: string;
  categoryId: string;
  name: string;
  /** Current `category-*` colour token, or null. */
  colour: string | null;
}

const reasonOf = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

// The ⋯ on a category heading in the sidebar: rename, change the colour, delete when it is empty. A category that still
// has entries cannot be deleted and the menu says how many to move or delete first.
export function CategoryHeadingMenu({
  bookId,
  categoryId,
  name,
  colour,
}: CategoryHeadingMenuProps) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(name);
  const [error, setError] = useState<string | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
  const guard = async (action: () => Promise<unknown>, fallback: string) => {
    try {
      await action();
      setError(null);
      await refresh();
      return true;
    } catch (e) {
      setError(reasonOf(e, fallback));
      return false;
    }
  };
  const close = () => {
    setOpen(false);
    setError(null);
  };
  const rename = async () => {
    const text = draft.trim();
    if (text === '') {
      setError('A category needs a name.');
      return;
    }
    if (text === name) {
      setError(null);
      return;
    }
    await guard(
      () => patchKbCategory(categoryId, { name: text }),
      'The category could not be renamed.',
    );
  };
  const remove = async () => {
    const done = await guard(async () => {
      const impact = await getKbCategoryDeleteImpact(categoryId);
      if (impact.entries > 0) {
        throw new Error(
          `Move or delete its ${impact.entries} ${impact.entries === 1 ? 'entry' : 'entries'} first.`,
        );
      }
      await deleteKbCategory(categoryId);
    }, 'The category could not be deleted.');
    if (done) {
      showToast({ message: `Deleted category ${name}` });
      close();
    }
  };

  return (
    <span className="relative">
      <button
        type="button"
        data-testid={kbTestIds.categoryMenu(categoryId)}
        aria-label={`${name} category menu`}
        title="Rename, recolour or delete this category"
        aria-expanded={open}
        onClick={() => (open ? close() : (setDraft(name), setOpen(true)))}
        className={`${iconButton} -my-1 h-[18px] w-[18px] [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-focus-within:opacity-100 [@media(hover:hover)]:group-hover:opacity-100 aria-expanded:opacity-100`}
      >
        ⋯
      </button>
      {open && (
        <div
          role="menu"
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.stopPropagation();
              close();
            }
          }}
          className="absolute right-0 z-30 mt-1 w-56 rounded border border-border bg-surface p-2 text-sm font-normal tracking-normal text-text normal-case shadow-lg"
        >
          <div className="flex gap-1">
            <input
              type="text"
              autoFocus
              data-testid={kbTestIds.categoryRenameInput}
              aria-label="Category name"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') void rename();
              }}
              className="h-7 min-w-0 flex-1 rounded border border-border bg-bg px-2 text-sm text-text"
            />
            <button
              type="button"
              data-testid={kbTestIds.categoryRenameSave}
              onClick={() => void rename()}
              className="h-7 rounded border border-border bg-transparent px-2 text-xs text-text hover:bg-accent-soft"
            >
              Rename
            </button>
          </div>
          <div className="mt-2 flex gap-2" role="group" aria-label="Colour">
            {categoryColours.map((token) => (
              <button
                key={token}
                type="button"
                data-testid={kbTestIds.categoryColour(token)}
                aria-label={token}
                aria-pressed={colour === token}
                onClick={() =>
                  void guard(
                    () => patchKbCategory(categoryId, { colour: token }),
                    'The colour could not be changed.',
                  )
                }
                className="h-5 w-5 rounded-full border-2 border-transparent aria-pressed:border-text"
                style={{ background: `var(--${token})` }}
              />
            ))}
          </div>
          <button
            type="button"
            role="menuitem"
            data-testid={kbTestIds.categoryDelete(categoryId)}
            onClick={() => void remove()}
            className="mt-2 flex h-7 w-full items-center rounded-sm border-0 bg-transparent px-1 text-left text-sm text-danger hover:bg-accent-soft"
          >
            Delete category…
          </button>
          {error && (
            <p
              data-testid={kbTestIds.categoryMenuError}
              role="alert"
              className="m-0 mt-1 text-xs text-danger"
            >
              {error}
            </p>
          )}
        </div>
      )}
    </span>
  );
}
