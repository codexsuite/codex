import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import * as generated from './tokens.generated';
import { darkTheme, lightTheme, themeFor } from './themes';

const tokens = JSON.parse(
  readFileSync(resolve(__dirname, '../../../../design/tokens.json'), 'utf8'),
);
const touch = tokens.density.touch;
const without$ = (o: Record<string, unknown>) =>
  Object.fromEntries(Object.entries(o).filter(([k]) => !k.startsWith('$')));

// If these fail, run `node scripts/generate-theme.mjs` in src/Codex.Mobile.
describe('generated theme matches design/tokens.json', () => {
  it('has every UI colour in light and dark', () => {
    expect(generated.lightColors).toEqual(without$(tokens.color.light));
    expect(generated.darkColors).toEqual(without$(tokens.color.dark));
  });

  it('applies the touch type scale', () => {
    expect(generated.fontSize).toEqual({ ...tokens.font.size, ...touch['font-size'] });
    expect(generated.fontSize.base).toBe(15);
  });

  it('has weights as strings', () => {
    const expected = Object.fromEntries(
      Object.entries(tokens.font.weight).map(([k, v]) => [k, String(v)]),
    );
    expect(generated.fontWeight).toEqual(expected);
  });

  it('has the touch sizes', () => {
    expect(generated.sizes).toEqual({
      bar: touch['bar-height'],
      control: touch['control-height'],
      row: touch['row-height'],
      tab: touch['tab-height'],
      cover: touch['cover-width'],
    });
  });

  it('has space and radius', () => {
    expect(generated.space).toEqual(
      Object.fromEntries(Object.entries(tokens.space).map(([k, v]) => [k, v])),
    );
    expect(generated.radius).toEqual(tokens.radius);
  });

  it('has category and status maps', () => {
    expect(generated.categoryColors.light).toEqual(without$(tokens.category.light));
    expect(generated.categoryColors.dark).toEqual(without$(tokens.category.dark));
    expect(generated.statusColors).toEqual(tokens.status);
  });
});

describe('themeFor', () => {
  it('picks the scheme, defaulting to light', () => {
    expect(themeFor('dark')).toBe(darkTheme);
    expect(themeFor('light')).toBe(lightTheme);
    expect(themeFor(null)).toBe(lightTheme);
    expect(themeFor(undefined)).toBe(lightTheme);
  });

  it('exposes the token values', () => {
    expect(themeFor('dark').colors.bg).toBe(tokens.color.dark.bg);
    expect(themeFor('light').colors.accent).toBe(tokens.color.light.accent);
    expect(themeFor('dark').scheme).toBe('dark');
    expect(themeFor('light').text.lg).toBe(touch['font-size'].lg);
    expect(themeFor('light').size.tab).toBe(52);
  });

  it('resolves status colours against the active scheme', () => {
    expect(darkTheme.status.published).toBe(tokens.color.dark[tokens.status.published]);
    expect(lightTheme.status.draft).toBe(tokens.color.light.muted);
    expect(darkTheme.category.law).toBe(tokens.category.dark.law);
  });
});
