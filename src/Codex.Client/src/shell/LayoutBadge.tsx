import type { BookHeader } from '@codex/core/api/types';
import { shellTestIds } from './shellTestIds';

// The issues that block print export, from the book header. Today the only blocker is a copyright risk (the
// References badge in danger, `book_reference_summary.blocks_print_export`), so the count is that risk count.
// TODO: Layout's preflight (#13) adds its own blockers; the header carries the full count then.
export function exportBlockerCount(book: BookHeader): number {
  return book.referenceBadgeTone === 'danger' ? (book.referenceBadgeCount ?? 0) : 0;
}

// The Layout tab's chip (print.html `.badge`): a tinted danger chip with the blocker count, hidden at 0.
// Deliberately not the solid References badge.
export function LayoutBadge({ book }: { book: BookHeader }) {
  const count = exportBlockerCount(book);
  if (count === 0) return null;
  return (
    <span
      data-testid={shellTestIds.layoutBadge}
      title={`${count} ${count === 1 ? 'issue blocks' : 'issues block'} export`}
      className="ml-[5px] inline-grid h-4 min-w-4 place-items-center rounded-lg bg-[color-mix(in_srgb,var(--color-danger)_16%,transparent)] px-1 text-2xs font-semibold text-danger tabular-nums"
    >
      {count}
    </span>
  );
}
