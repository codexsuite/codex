import { configurePlatform, memoryStore, platform, type Platform } from '@codex/core/platform';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  defaultCollapsed,
  isActCollapsed,
  parseCollapsed,
  readCollapsed,
  writeCollapsed,
} from './collapsedActs';

describe('parseCollapsed', () => {
  it('ReadsAMapOfBooleans', () => {
    expect(parseCollapsed('{"a":true,"b":false}')).toEqual({ a: true, b: false });
  });

  it.each([null, '', 'nope', '[1]', '"x"', 'null'])('Raw_%s_ShouldBeNothingStored', (raw) => {
    expect(parseCollapsed(raw)).toEqual({});
  });

  it('DropsValuesThatAreNotBooleans', () => {
    expect(parseCollapsed('{"a":true,"b":"yes","c":1}')).toEqual({ a: true });
  });
});

describe('isActCollapsed', () => {
  it('StartsEveryActFromTheThirdCollapsed', () => {
    expect([0, 1, 2, 3].map(defaultCollapsed)).toEqual([false, false, true, true]);
  });

  it('LetsAStoredChoiceWinOverTheDefault', () => {
    expect(isActCollapsed({ a: false }, 'a', 5)).toBe(false);
    expect(isActCollapsed({ a: true }, 'a', 0)).toBe(true);
    expect(isActCollapsed({}, 'a', 2)).toBe(true);
  });
});

describe('the stored choice', () => {
  let original: Platform;
  beforeEach(() => {
    original = platform();
    configurePlatform({
      ...original,
      persistentStore: (() => {
        const store = memoryStore();
        return () => store;
      })(),
    });
  });
  afterEach(() => configurePlatform(original));

  it('IsKeptPerBook', () => {
    writeCollapsed('book-1', { a: true });

    expect(readCollapsed('book-1')).toEqual({ a: true });
    expect(readCollapsed('book-2')).toEqual({});
  });

  it('ReadsAsNothingStoredWhenStorageThrows', () => {
    configurePlatform({
      ...original,
      persistentStore: () => {
        throw new Error('blocked');
      },
    });

    expect(readCollapsed('book-1')).toEqual({});
    expect(() => writeCollapsed('book-1', { a: true })).not.toThrow();
  });
});
