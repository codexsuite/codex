import { describe, expect, it } from 'vitest';
import { overline, wordsLabel } from './bookFacts';

describe('wordsLabel', () => {
  it('Words_WhenUnderAThousand_ShouldBePlain', () => {
    expect(wordsLabel(0)).toBe('0 words');
    expect(wordsLabel(999)).toBe('999 words');
  });

  it('Words_WhenAThousandOrMore_ShouldUseK', () => {
    expect(wordsLabel(13703)).toBe('13.7k words');
    expect(wordsLabel(72300)).toBe('72.3k words');
  });
});

describe('overline', () => {
  it('Overline_ShouldPadSeriesNumber', () => {
    expect(overline(3)).toBe('Book 003');
    expect(overline(null)).toBeNull();
  });
});
