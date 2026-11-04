import { describe, expect, it } from 'vitest';
import { displayVersion } from './displayVersion';

describe('displayVersion', () => {
  it.each([
    ['1.0.0+abc123', '1.0.0'],
    ['2.1.0', '2.1.0'],
    ['unknown', 'unknown'],
  ])('%s -> %s', (version, expected) => {
    expect(displayVersion(version)).toBe(expected);
  });
});
