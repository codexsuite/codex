import { describe, expect, it } from 'vitest';
import type { SeriesNumberChange, SeriesOrderEntry } from '../../api/settings';
import { numberLine, padded, wasNumbers } from './seriesNumbers';

const order: SeriesOrderEntry[] = [
  { id: 'a', title: 'Ember Oath', number: 1, status: 'published' },
  { id: 'c', title: 'Hollow Throne', number: 2, status: 'revising' },
  { id: 'b', title: 'Iron Litany', number: 3, status: 'published' },
];

describe('seriesNumbers', () => {
  it('Padded_WhenSingleDigit_ShouldHaveThreeDigits', () => {
    expect(padded(3)).toBe('003');
  });

  it('NumberLine_WhenNotLast_ShouldNameTheBookItComesBefore', () => {
    expect(numberLine(order, 2)).toBe('Book 002 · before Iron Litany');
  });

  it('NumberLine_WhenLast_ShouldSayLast', () => {
    expect(numberLine(order, 3)).toBe('Book 003 · last in the series');
  });

  it('WasNumbers_WhenBooksShifted_ShouldListOnlyTheMovedOnes', () => {
    const change: SeriesNumberChange = {
      bookId: 'c',
      seriesId: 's',
      number: 2,
      previousNumber: 3,
      books: [
        { id: 'a', number: 1, previousNumber: 1 },
        { id: 'c', number: 2, previousNumber: 3 },
        { id: 'b', number: 3, previousNumber: 2 },
      ],
    };
    expect([...wasNumbers(change)]).toEqual([
      ['c', 3],
      ['b', 2],
    ]);
  });

  it('WasNumbers_WhenNoChange_ShouldBeEmpty', () => {
    expect(wasNumbers(null).size).toBe(0);
  });
});
