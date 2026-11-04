import { describe, expect, it } from 'vitest';
import { plainInstruction, sceneLabel, wordsLabel } from './format';

describe('wordsLabel', () => {
  it.each([
    [0, '0 words'],
    [1, '1 word'],
    [999, '999 words'],
    [3860, '3,860 words'],
    [1234567, '1,234,567 words'],
  ])('wordsLabel_When%i_ShouldRead%s', (words, expected) => {
    expect(wordsLabel(words)).toBe(expected);
  });
});

describe('sceneLabel', () => {
  it.each([
    ['3.2', 'Scene 2'],
    ['1.1', 'Scene 1'],
    ['2', 'Scene 2'],
  ])('sceneLabel_When%s_ShouldRead%s', (number, expected) => {
    expect(sceneLabel(number)).toBe(expected);
  });
});

describe('plainInstruction', () => {
  it('plainInstruction_WhenLinksAndEmphasis_ShouldKeepOnlyTheWords', () => {
    expect(
      plainInstruction('Tobin counts the **guards**: [the Abbot](kb:abc) rings *the* bell.'),
    ).toBe('Tobin counts the guards: the Abbot rings the bell.');
  });

  it('plainInstruction_WhenSeveralLines_ShouldJoinThemWithSpaces', () => {
    expect(plainInstruction('One.\n\nTwo.\n')).toBe('One. Two.');
  });

  it('plainInstruction_WhenAnUnpairedUnderscore_ShouldLeaveIt', () => {
    expect(plainInstruction('snake_case word')).toBe('snake_case word');
  });
});
