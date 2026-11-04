import { libraryTestIds } from './testIds';

// Nothing came back. With a search, offer to clear it; with only a status filter or an empty library, say so.
export function NoResults({ q, onClear }: { q: string; onClear: () => void }) {
  return (
    <div data-testid={libraryTestIds.noResults} className="flex flex-col items-start gap-2 p-4">
      <p data-testid={libraryTestIds.empty} className="m-0 text-sm text-muted">
        {q === '' ? 'No books to show.' : `No books match "${q}".`}
      </p>
      {q !== '' && (
        <button
          type="button"
          data-testid={libraryTestIds.clearSearch}
          onClick={onClear}
          className="cursor-pointer rounded-sm border border-border bg-surface px-2 py-1 text-sm text-accent"
        >
          Clear search
        </button>
      )}
    </div>
  );
}
