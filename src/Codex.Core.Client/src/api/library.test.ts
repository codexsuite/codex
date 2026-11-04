import { describe, expect, it } from 'vitest';
import { defaultLibraryParams, libraryPath, parseCoverPalette } from './library';

describe('parseCoverPalette', () => {
  it('Palette_WhenKnown_ShouldPassThrough', () => {
    expect(parseCoverPalette('cover-8')).toBe('cover-8');
  });

  it('Palette_WhenUnknown_ShouldThrow', () => {
    expect(() => parseCoverPalette('cover-9')).toThrow(/Unknown cover palette "cover-9"/);
    expect(() => parseCoverPalette('')).toThrow();
  });
});

describe('libraryPath', () => {
  it('Path_WhenDefaults_ShouldHaveNoQuery', () => {
    expect(libraryPath(defaultLibraryParams)).toBe('/api/library');
  });

  it('Path_WhenFiltered_ShouldSendOnlyNonDefaults', () => {
    expect(libraryPath({ q: 'tide wa', sort: 'title', group: 'series', status: 'writing' })).toBe(
      '/api/library?q=tide+wa&sort=title&status=writing',
    );
  });
});
