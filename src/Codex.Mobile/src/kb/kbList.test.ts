import { describe, expect, it } from 'vitest';
import type { Kb } from '@codex/core/api/kb';
import { allFilter, buildKbList, sameFilter } from './kbList';

const category = (id: string, name: string, position: string, fromSeries = false) => ({
  id,
  name,
  tag: null,
  colour: `category-${id}`,
  position,
  fieldDefs: [],
  fromSeries,
  seriesName: fromSeries ? 'The Ashen Crown' : null,
});
const entry = (
  id: string,
  categoryId: string,
  name: string,
  extra: Partial<Kb['entries'][number]> = {},
) => ({
  id,
  categoryId,
  name,
  aliases: [],
  sceneCount: 1,
  useCount: 1,
  fromSeries: false,
  seriesName: null,
  alwaysInContext: false,
  summaryLine: '',
  ...extra,
});

const kb: Kb = {
  categories: [
    category('person', 'Characters', 'b'),
    category('story', 'Places', 'c'),
    category('law', 'Rules', 'a', true),
  ],
  entries: [
    entry('maren', 'person', 'Maren', {
      aliases: ['The Queen'],
      alwaysInContext: true,
      summaryLine: 'Rules from the north.',
    }),
    entry('tobin', 'person', 'Tobin', { sceneCount: 0 }),
    entry('hall', 'story', 'The Hall'),
    entry('oath', 'law', 'Oath', { fromSeries: true, seriesName: 'The Ashen Crown' }),
  ],
};

const names = (m: ReturnType<typeof buildKbList>) =>
  m.sections.flatMap((s) => s.groups.flatMap((g) => g.rows.map((r) => r.name)));

describe('buildKbList', () => {
  it('Build_WithAllFilter_ShouldGroupByCategoryWithSeriesFirst', () => {
    const model = buildKbList(kb, '', allFilter);

    expect(model.sections.map((s) => s.key)).toEqual(['series', 'book']);
    expect(model.sections[0].seriesName).toBe('The Ashen Crown');
    expect(model.sections[1].groups.map((g) => g.name)).toEqual(['Characters', 'Places']);
    expect(model.shown).toBe(4);
  });

  it('Build_ShouldCarryTheAlwaysTagAliasesAndSceneCountOntoRows', () => {
    const maren = buildKbList(kb, '', allFilter).sections[1].groups[0].rows[0];

    expect(maren).toEqual({
      id: 'maren',
      name: 'Maren',
      alwaysInContext: true,
      summaryLine: 'Rules from the north.',
      sceneCount: 1,
    });
  });

  it('Build_WithAllFilter_ShouldKeepACategoryNobodyHasAnEntryInForItsPlus', () => {
    const withEmpty: Kb = { ...kb, categories: [...kb.categories, category('lore', 'Lore', 'd')] };

    const groups = buildKbList(withEmpty, '', allFilter).sections[1].groups;

    expect(groups.map((g) => g.name)).toEqual(['Characters', 'Places', 'Lore']);
    expect(groups[2].rows).toEqual([]);
  });

  it('Build_WithUnused_ShouldDropEmptyCategories', () => {
    const withEmpty: Kb = { ...kb, categories: [...kb.categories, category('lore', 'Lore', 'd')] };

    const groups = buildKbList(withEmpty, '', { kind: 'unused' }).sections[0].groups;

    expect(groups.map((g) => g.name)).toEqual(['Characters']);
  });

  it('Build_Chips_ShouldListAllEachCategoryWithItsCountAndUnused', () => {
    const chips = buildKbList(kb, '', allFilter).chips;

    expect(chips.map((c) => [c.key, c.count])).toEqual([
      ['all', 4],
      ['law', 1],
      ['person', 2],
      ['story', 1],
      ['unused', 1],
    ]);
  });

  it('Build_WithACategoryFilter_ShouldKeepOnlyThatCategory', () => {
    const model = buildKbList(kb, '', { kind: 'category', categoryId: 'story' });

    expect(names(model)).toEqual(['The Hall']);
  });

  it('Build_WithAnEmptyCategoryFilter_ShouldKeepTheEmptyGroupForItsPlus', () => {
    const empty: Kb = { categories: [category('person', 'Characters', 'a')], entries: [] };

    const model = buildKbList(empty, '', { kind: 'category', categoryId: 'person' });

    expect(model.sections[0].groups).toEqual([
      { id: 'person', name: 'Characters', colour: 'category-person', rows: [] },
    ]);
  });

  it('Build_WithUnused_ShouldListOnlyEntriesNoSceneUses', () => {
    const model = buildKbList(kb, '', { kind: 'unused' });

    expect(names(model)).toEqual(['Tobin']);
  });

  it('Build_WithASearch_ShouldMatchNamesAndAliases', () => {
    expect(names(buildKbList(kb, 'queen', allFilter))).toEqual(['Maren']);
    expect(names(buildKbList(kb, 'zzz', allFilter))).toEqual([]);
  });

  it('Build_WhenNothingMatches_ShouldStillReportTheTotal', () => {
    const model = buildKbList(kb, 'zzz', allFilter);

    expect(model.shown).toBe(0);
    expect(model.totalEntries).toBe(4);
  });
});

describe('sameFilter', () => {
  it('SameFilter_ShouldCompareKindAndCategory', () => {
    expect(sameFilter(allFilter, allFilter)).toBe(true);
    expect(sameFilter(allFilter, { kind: 'unused' })).toBe(false);
    expect(
      sameFilter({ kind: 'category', categoryId: 'a' }, { kind: 'category', categoryId: 'a' }),
    ).toBe(true);
    expect(
      sameFilter({ kind: 'category', categoryId: 'a' }, { kind: 'category', categoryId: 'b' }),
    ).toBe(false);
  });
});
