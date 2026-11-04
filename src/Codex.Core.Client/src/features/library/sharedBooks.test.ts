import { describe, expect, it } from 'vitest';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import type { Library, LibrarySeries } from '../../api/types';
import { filterLibrary, readFilter, seriesShare, withFilter } from './sharedBooks';

const fixture = library as Library;
const mara = { id: 'mara', displayName: 'Mara Lind' };

function sharedSeries(roles: string[]): LibrarySeries {
  const base = fixture.series[0];
  return {
    ...base,
    books: roles.map((role, i) => ({ ...base.books[0], id: `b${i}`, role, owner: mara })),
  };
}

describe('library filter in the hash', () => {
  it('Filter_WhenReadFromTheHash_ShouldDefaultToAll', () => {
    expect(readFilter('')).toBe('all');
    expect(readFilter('#filter=shared')).toBe('shared');
    expect(readFilter('#series=s1&filter=mine')).toBe('mine');
    expect(readFilter('#filter=nonsense')).toBe('all');
  });

  it('Filter_WhenWritten_ShouldKeepOtherKeysAndDropAll', () => {
    expect(withFilter('', 'shared')).toBe('#filter=shared');
    expect(withFilter('#series=s1', 'mine')).toBe('#series=s1&filter=mine');
    expect(withFilter('#filter=mine', 'all')).toBe('');
  });
});

describe('filterLibrary', () => {
  it('Filter_WhenMineOrShared_ShouldSplitByRole', () => {
    // The fixture has one shared standalone book (Northbound, write, Ana) and an owned series.
    const shared = filterLibrary(fixture, 'shared');
    expect(shared.series).toEqual([]);
    expect(shared.standalone.map((b) => b.title)).toEqual(['Northbound']);
    const mine = filterLibrary(fixture, 'mine');
    expect(mine.series).toHaveLength(1);
    expect(mine.standalone).toEqual([]);
    expect(filterLibrary(fixture, 'all')).toBe(fixture);
  });
});

describe('seriesShare', () => {
  it('Share_WhenOwnSeries_ShouldBeNull', () => {
    expect(seriesShare(fixture.series[0])).toBeNull();
  });

  it('Share_WhenSomeoneElsesSeries_ShouldNameThemAndTakeTheLowestRole', () => {
    expect(seriesShare(sharedSeries(['write', 'comment', 'write']))).toEqual({
      owner: 'Mara Lind',
      role: 'comment',
    });
  });
});
