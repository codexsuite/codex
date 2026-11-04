import type { BookHeader } from '@codex/core/api/types';
import { shellTestIds } from './shellTestIds';

const badgeTones: Record<string, string> = {
  danger: 'bg-danger text-surface',
  warn: 'bg-warn text-surface',
};

// The References count badge from the book header (design/README.md: danger for copyright risks, otherwise warn for
// sources to check). Hidden when there is nothing to count. Refetch the header (bookKey) after a clearance change.
export function ReferencesBadge({ book }: { book: BookHeader }) {
  const tone = book.referenceBadgeTone === null ? undefined : badgeTones[book.referenceBadgeTone];
  if (!book.referenceBadgeCount || tone === undefined) return null;
  return (
    <span
      data-testid={shellTestIds.referencesBadge}
      data-tone={book.referenceBadgeTone}
      className={`ml-1 inline-flex min-w-4 items-center justify-center rounded-full px-1 text-2xs leading-4 font-semibold ${tone}`}
    >
      {book.referenceBadgeCount}
    </span>
  );
}
