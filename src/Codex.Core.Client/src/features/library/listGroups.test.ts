import { describe, expect, it } from 'vitest';
import library from '../../../../Codex.TestSupport/Fixtures/library.json';
import { listGroups } from './listGroups';

describe('listGroups', () => {
  it('Groups_WhenBySeries_ShouldListEachSeriesThenStandalone', () => {
    const groups = listGroups(library, 'series', 'lastEdited');
    expect(groups.map((g) => g.label)).toEqual([library.series[0].name, 'Standalone']);
  });

  it('Groups_WhenNone_ShouldBeOneUnlabelledGroupSortedByTitle', () => {
    const [only, ...rest] = listGroups(library, 'none', 'title');
    expect(rest).toHaveLength(0);
    expect(only.label).toBeNull();
    const titles = only.books.map((b) => b.title);
    expect(titles).toEqual([...titles].sort((a, b) => a.localeCompare(b)));
  });

  it('Groups_WhenByStatus_ShouldBucketByStatus', () => {
    const groups = listGroups(library, 'status', 'lastEdited');
    for (const g of groups) expect(new Set(g.books.map((b) => b.status))).toEqual(new Set([g.key]));
  });
});
