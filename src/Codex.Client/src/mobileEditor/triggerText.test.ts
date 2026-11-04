import { describe, expect, it } from 'vitest';
import { triggerText } from './triggerText';

describe('triggerText', () => {
  it.each([
    ['', '/', '/'],
    [' ', '@', '@'],
    ['\n', '/', '/'],
    ['d', '/', ' /'],
    ['.', '@', ' @'],
  ] as const)(
    'triggerText_WhenTheCharacterBeforeIs%j_For%s_ShouldInsert%j',
    (before, trigger, expected) => {
      expect(triggerText(before, trigger)).toBe(expected);
    },
  );
});
