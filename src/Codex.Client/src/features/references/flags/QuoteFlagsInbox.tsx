import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getQuoteFlags, quoteFlagsKey, type QuoteFlag } from '@codex/core/api/quoteFlags';
import { RefIcon } from '../RefIcon';
import { refsTestIds } from '../testIds';
import { QuoteFlagCard } from './QuoteFlagCard';

// The manuscript scan's list, shown in the detail panel's place: quotation marks with no source marker, and passages
// that match a source's attached text.
export function QuoteFlagsInbox({ bookId, onClose }: { bookId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const flags = useQuery({
    queryKey: quoteFlagsKey(bookId),
    queryFn: ({ signal }) => getQuoteFlags(bookId, { signal }),
  });
  const items = flags.data ?? [];
  const resolved = (remaining: QuoteFlag[]) =>
    queryClient.setQueryData(quoteFlagsKey(bookId), remaining);

  return (
    <section data-testid={refsTestIds.flagPanel} aria-label="Possible unflagged quotes">
      <header className="sticky top-0 z-[3] border-b border-border bg-surface px-3 pt-3 pb-2">
        <div className="flex items-center gap-1.5">
          <span className="min-w-0 flex-1 truncate text-2xs font-medium tracking-[.02em] text-muted uppercase">
            Manuscript scan · on every save
          </span>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="inline-grid size-[22px] cursor-pointer place-items-center rounded-xs text-muted hover:bg-surface-2 hover:text-text"
          >
            <RefIcon name="close" />
          </button>
        </div>
        <h2 className="mt-1 mb-0.5 text-md leading-[1.3] font-semibold">
          Possible unflagged quotes
        </h2>
        <div className="text-xs text-muted">
          Text in quotation marks without a marker, and passages that match the source text attached
          to a reference.
        </div>
      </header>
      {flags.data !== undefined && items.length === 0 ? (
        <p
          data-testid={refsTestIds.flagEmpty}
          className="m-0 px-4 py-15 text-center text-sm text-muted"
        >
          Nothing to review. Every quote in the book has a source.
        </p>
      ) : (
        items.map((flag) => (
          <QuoteFlagCard key={flag.id} bookId={bookId} flag={flag} onResolved={resolved} />
        ))
      )}
    </section>
  );
}
