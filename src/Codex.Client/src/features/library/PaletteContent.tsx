import { Command } from 'cmdk';
import { useNavigate } from 'react-router-dom';
import { defaultLibraryParams } from '@codex/core/api/library';
import { useLibrary } from '../../api/libraryQueries';
import type { LibraryBook } from '@codex/core/api/types';
import { usePalette } from '../../shell/CommandPalette';
import { newBookLink, seriesLink } from '@codex/core/shell/links';
import { currentTheme, setTheme } from '../../shell/theme';
import { openLink, overline } from '@codex/core/features/library/bookFacts';
import { importLink, newSeriesLink } from './libraryLinks';
import { libraryTestIds } from './testIds';
import { isShared } from '@codex/core/features/library/sharedBooks';

const group =
  '[&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:text-2xs [&_[cmdk-group-heading]]:font-medium [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:text-muted';
const item =
  'flex cursor-pointer items-center gap-2 px-3 py-1.5 text-sm text-text aria-selected:bg-accent-soft';

function bookHint(book: LibraryBook): string {
  const above = overline(book.seriesNumber);
  const where = book.seriesName && above ? `${book.seriesName} · ${above}` : 'Standalone';
  // A book shared with you names its owner (#80).
  return isShared(book) ? `${where} · ${book.owner.displayName}` : where;
}

// ⌘K content: books and series from the Library, then actions. Typing filters (cmdk), Enter runs the highlighted
// row, Esc is the frame's. Rendered by the palette itself, so it works on every screen.
export function PaletteContent() {
  const navigate = useNavigate();
  const { setOpen } = usePalette();
  const library = useLibrary(defaultLibraryParams);
  const go = (to: string) => {
    setOpen(false);
    void navigate(to);
  };
  const books = library.data
    ? [...library.data.series.flatMap((s) => s.books), ...library.data.standalone]
    : [];

  return (
    <Command label="Command palette" className="flex flex-col">
      <Command.Input
        data-testid={libraryTestIds.paletteInput}
        placeholder="Search books, series and actions…"
        className="h-10 w-full border-0 border-b border-border bg-transparent px-3 text-base text-text outline-none"
      />
      <Command.List
        data-testid={libraryTestIds.paletteList}
        className="max-h-[50vh] overflow-auto py-1"
      >
        <Command.Empty
          data-testid={libraryTestIds.paletteEmpty}
          className="px-3 py-4 text-sm text-muted"
        >
          Nothing matches.
        </Command.Empty>
        {library.isError && (
          <div
            role="alert"
            data-testid={libraryTestIds.paletteError}
            className="px-3 py-2 text-sm text-danger"
          >
            The Library could not be loaded: {library.error.message}
          </div>
        )}
        {books.length > 0 && (
          <Command.Group heading="Books" className={group}>
            {books.map((book) => (
              <Command.Item
                key={book.id}
                value={`${book.title} ${book.seriesName ?? ''} ${book.id}`}
                data-testid={libraryTestIds.paletteBook(book.id)}
                onSelect={() => go(openLink(book))}
                className={item}
              >
                <span className="flex-1 truncate">{book.title}</span>
                <span className="text-xs text-muted">{bookHint(book)}</span>
              </Command.Item>
            ))}
          </Command.Group>
        )}
        {library.data && library.data.series.length > 0 && (
          <Command.Group heading="Series" className={group}>
            {library.data.series.map((series) => (
              <Command.Item
                key={series.id}
                value={`${series.name} series ${series.id}`}
                data-testid={libraryTestIds.paletteSeries(series.id)}
                onSelect={() => go(seriesLink(series.id))}
                className={item}
              >
                <span className="flex-1 truncate">{series.name}</span>
                <span className="text-xs text-muted">{series.bookCount} books</span>
              </Command.Item>
            ))}
          </Command.Group>
        )}
        <Command.Group heading="Actions" className={group}>
          <Command.Item
            value="Create novel new book"
            data-testid={libraryTestIds.paletteAction('new-book')}
            onSelect={() => go(newBookLink())}
            className={item}
          >
            New book
          </Command.Item>
          <Command.Item
            value="Create series new series"
            data-testid={libraryTestIds.paletteAction('new-series')}
            onSelect={() => go(newSeriesLink())}
            className={item}
          >
            New series…
          </Command.Item>
          <Command.Item
            value="Import a book"
            data-testid={libraryTestIds.paletteAction('import')}
            onSelect={() => go(importLink())}
            className={item}
          >
            Import a book…
          </Command.Item>
          <Command.Item
            value="Toggle theme dark light"
            data-testid={libraryTestIds.paletteAction('theme')}
            onSelect={() => {
              setTheme(currentTheme() === 'dark' ? 'light' : 'dark');
              setOpen(false);
            }}
            className={item}
          >
            Toggle theme
          </Command.Item>
        </Command.Group>
      </Command.List>
    </Command>
  );
}
