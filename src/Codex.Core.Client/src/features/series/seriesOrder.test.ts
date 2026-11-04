import { describe, expect, it } from 'vitest';
import { moveBy, paddedNumber, sameOrder } from './seriesOrder';

describe('seriesOrder', () => {
  it('moveBy_WhenMovingUp_ShouldSwapWithPrevious', () => {
    expect(moveBy(['a', 'b', 'c'], 1, -1)).toEqual(['b', 'a', 'c']);
  });
  it('moveBy_WhenMovingDown_ShouldSwapWithNext', () => {
    expect(moveBy(['a', 'b', 'c'], 1, 1)).toEqual(['a', 'c', 'b']);
  });
  it('moveBy_WhenOffTheEnd_ShouldReturnSameArray', () => {
    const items = ['a', 'b'];
    expect(moveBy(items, 0, -1)).toBe(items);
    expect(moveBy(items, 1, 1)).toBe(items);
  });
  it('sameOrder_ShouldCompareElementwise', () => {
    expect(sameOrder(['a', 'b'], ['a', 'b'])).toBe(true);
    expect(sameOrder(['a', 'b'], ['b', 'a'])).toBe(false);
  });
  it('paddedNumber_ShouldPadToThree', () => {
    expect(paddedNumber(7)).toBe('007');
  });
});
