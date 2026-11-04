import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import { settingsApi } from '@codex/core/api/settings';
import type { LibraryBook } from '@codex/core/api/types';
import { DeleteBookDialog } from './DeleteBookDialog';
import { libraryTestIds } from './testIds';

const button =
  'h-6 cursor-pointer rounded border border-border bg-surface px-2 text-xs text-text hover:bg-surface-2';

// An archived book's row or tile in the Library's Archived list: Restore, and a ⋯ menu holding Delete permanently….
// Both are separate controls next to the book, so a menu another lane adds stays a separate item.
export function ArchivedActions({ book }: { book: LibraryBook }) {
  const queryClient = useQueryClient();
  const [menu, setMenu] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const menuButton = useRef<HTMLButtonElement>(null);
  const wasDeleting = useRef(false);
  // The dialog is modal: once it is gone, focus goes back to the control that opened the menu.
  useEffect(() => {
    if (wasDeleting.current && !deleting) menuButton.current?.focus();
    wasDeleting.current = deleting;
  }, [deleting]);

  const restore = async () => {
    setError(null);
    try {
      await settingsApi.restore(book.id);
      await queryClient.invalidateQueries({ queryKey: bookKey(book.id) });
      await queryClient.invalidateQueries({ queryKey: ['library'] });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The book could not be restored.');
    }
  };

  return (
    <span
      data-testid={libraryTestIds.archivedActions(book.id)}
      className="relative flex flex-wrap items-center gap-1"
    >
      <button
        type="button"
        data-testid={libraryTestIds.restoreBook(book.id)}
        onClick={() => void restore()}
        className={button}
      >
        Restore
      </button>
      <button
        type="button"
        ref={menuButton}
        data-testid={libraryTestIds.bookMenu(book.id)}
        aria-label={`More for ${book.title}`}
        aria-expanded={menu}
        onClick={() => setMenu(!menu)}
        className={button}
      >
        ⋯
      </button>
      {menu && (
        <span className="absolute top-7 left-0 z-20 w-48 rounded border border-border bg-surface py-1 shadow-lg">
          <button
            type="button"
            data-testid={libraryTestIds.deleteBookItem(book.id)}
            onClick={() => {
              setMenu(false);
              setDeleting(true);
            }}
            className="flex h-7 w-full cursor-pointer items-center border-0 bg-transparent px-3 text-left text-sm text-danger hover:bg-accent-soft"
          >
            Delete permanently…
          </button>
        </span>
      )}
      {error && (
        <span role="alert" className="text-xs text-danger">
          {error}
        </span>
      )}
      {deleting && (
        <DeleteBookDialog bookId={book.id} title={book.title} onClose={() => setDeleting(false)} />
      )}
    </span>
  );
}
