import { describe, expect, it } from 'vitest';
import { formatMegabytes, progressPercent } from './formatBytes';

describe('formatMegabytes', () => {
  it.each([
    [77_704_715, '78 MB'],
    [147_964_211, '148 MB'],
    [487_614_201, '488 MB'],
  ])('Bytes_%i_ShouldBeShownAs_%s', (bytes, text) => {
    expect(formatMegabytes(bytes)).toBe(text);
  });
});

describe('progressPercent', () => {
  it('WhenHalfReceived_ShouldBeFifty', () => {
    expect(progressPercent(50, 100)).toBe(50);
  });
  it('WhenTheTotalIsUnknown_ShouldBeZero', () => {
    expect(progressPercent(50, 0)).toBe(0);
  });
  it('WhenMoreThanTheTotalArrived_ShouldStopAtOneHundred', () => {
    expect(progressPercent(150, 100)).toBe(100);
  });
  it('WhenRoundingDown_ShouldNotReachOneHundredBeforeTheEnd', () => {
    expect(progressPercent(999, 1000)).toBe(99);
  });
});
