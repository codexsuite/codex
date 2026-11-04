import { useQuery } from '@tanstack/react-query';
import { getQuoteFlags, quoteFlagsKey } from '@codex/core/api/quoteFlags';
import { RefIcon } from '../RefIcon';
import { refsTestIds } from '../testIds';

// "n possible unflagged quotes" at the end of the summary strip: opens the manuscript scan's list in the panel.
// Hidden when the scan found nothing, unless the list is open (it then says there is nothing left).
export function QuoteFlagsButton({
  bookId,
  open,
  onToggle,
}: {
  bookId: string;
  open: boolean;
  onToggle: () => void;
}) {
  const flags = useQuery({
    queryKey: quoteFlagsKey(bookId),
    queryFn: ({ signal }) => getQuoteFlags(bookId, { signal }),
  });

  if (flags.error !== null) {
    return (
      <span role="alert" data-testid={refsTestIds.flagError} className="text-danger">
        Possible unflagged quotes could not be loaded: {flags.error.message}
      </span>
    );
  }
  const count = flags.data?.length ?? 0;
  if (count === 0 && !open) return null;
  return (
    <button
      type="button"
      aria-pressed={open}
      data-testid={refsTestIds.flagInbox}
      onClick={onToggle}
      className="inline-flex h-6 cursor-pointer items-center gap-1.5 rounded-sm border border-[color-mix(in_srgb,var(--color-warn)_45%,var(--color-border))] bg-[color-mix(in_srgb,var(--color-warn)_8%,var(--color-surface))] px-2 text-xs font-medium whitespace-nowrap text-text aria-pressed:shadow-[0_0_0_2px_color-mix(in_srgb,var(--color-warn)_35%,transparent)]"
    >
      <RefIcon name="flag" className="size-3 text-warn" />
      {count} possible unflagged {count === 1 ? 'quote' : 'quotes'}
    </button>
  );
}
