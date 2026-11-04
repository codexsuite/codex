import { describe, expect, it } from 'vitest';
import { countsLabel } from './scope';

describe('countsLabel', () => {
  it('CountsLabel_ShouldGiveWordsPagesAndReadTime', () => {
    expect(countsLabel({ words: 634 })).toBe('634 words · 3 pages · 3m read');
    expect(countsLabel({ words: 13703 })).toBe('13,703 words · 55 pages · 58m read');
  });

  it('CountsLabel_WhenAnHourOrMore_ShouldGiveHoursAndMinutes', () => {
    expect(countsLabel({ words: 20000 })).toBe('20,000 words · 80 pages · 1h 24m read');
  });

  it('CountsLabel_WhenOnePageOrNone_ShouldCountThem', () => {
    expect(countsLabel({ words: 200 })).toBe('200 words · 1 page · 1m read');
    expect(countsLabel({ words: 0 })).toBe('0 words · 0 pages · 0m read');
  });
});
