import { describe, expect, it } from 'vitest';
import { toneOf } from './labelTone';

describe('toneOf', () => {
  it.each(['ok', 'info', 'warn', 'danger'])('Colour_%s_ShouldBeATone', (color) => {
    expect(toneOf(color)).toBe(color);
  });

  it.each([null, '', '#ff0000', 'purple'])('Colour_%s_ShouldBeHollow', (color) => {
    expect(toneOf(color)).toBeNull();
  });
});
