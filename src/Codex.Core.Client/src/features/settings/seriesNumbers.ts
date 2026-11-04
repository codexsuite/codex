import type { SeriesNumberChange, SeriesOrderEntry } from '../../api/settings';

/** `3` -> `003`, as the Library prints series numbers. */
export const padded = (n: number) => String(n).padStart(3, '0');

/** `Book 003 · before Hollow Throne`, or `· last in the series`. */
export function numberLine(order: SeriesOrderEntry[], number: number): string {
  const after = order.find((e) => e.number === number + 1);
  return `Book ${padded(number)} · ${after ? `before ${after.title}` : 'last in the series'}`;
}

/** The numbers each book had before the last change (only books whose number moved). */
export function wasNumbers(change: SeriesNumberChange | null): Map<string, number> {
  const was = new Map<string, number>();
  for (const book of change?.books ?? []) {
    if (book.number !== book.previousNumber) was.set(book.id, book.previousNumber);
  }
  return was;
}
