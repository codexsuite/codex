import { describe, expect, it } from 'vitest';
import type { Kb } from '../../api/kb';
import { buildKbSidebar } from './kbSidebar';

const category = (id: string, name: string, position: string, fromSeries = false) => ({
  id,
  name,
  tag: null,
  colour: null,
  position,
  fieldDefs: [],
  fromSeries,
  seriesName: fromSeries ? 'Saga' : null,
});
const entry = (id: string, categoryId: string, name: string, fromSeries = false) => ({
  id,
  categoryId,
  name,
  aliases: [],
  sceneCount: 1,
  useCount: 1,
  fromSeries,
  seriesName: fromSeries ? 'Saga' : null,
  alwaysInContext: false,
  summaryLine: '',
});

describe('buildKbSidebar', () => {
  it('lists empty categories only when there is no search', () => {
    const kb: Kb = { categories: [category('c1', 'Characters', 'a0')], entries: [] };
    expect(buildKbSidebar(kb, '').sections[0].groups.map((g) => g.name)).toEqual(['Characters']);
    expect(buildKbSidebar(kb, 'x').sections).toEqual([]);
  });

  it('puts series entries first and orders categories by position', () => {
    const kb: Kb = {
      categories: [category('c2', 'Places', 'a1'), category('c1', 'People', 'a0')],
      entries: [
        entry('e1', 'c2', 'Harbour'),
        entry('e2', 'c1', 'Ward', true),
        entry('e3', 'c1', 'Ada'),
      ],
    };
    const { sections } = buildKbSidebar(kb, '');
    expect(sections.map((s) => s.key)).toEqual(['series', 'book']);
    expect(sections[0].seriesName).toBe('Saga');
    expect(sections[1].groups.map((g) => g.name)).toEqual(['People', 'Places']);
  });

  it('lists an empty series-only category under the series section', () => {
    const kb: Kb = {
      categories: [category('c1', 'Characters', 'a0', true), category('c2', 'Places', 'a1')],
      entries: [],
    };
    const { sections } = buildKbSidebar(kb, '');
    expect(sections.map((s) => [s.key, s.seriesName, s.groups.map((g) => g.name)])).toEqual([
      ['series', 'Saga', ['Characters']],
      ['book', null, ['Places']],
    ]);
  });
});

describe('buildKbSidebar with hidden categories', () => {
  const kb: Kb = {
    categories: [
      category('c1', 'People', 'a0', true),
      category('c2', 'Places', 'a1'),
      category('c3', 'Laws', 'a2'),
    ],
    entries: [
      entry('e1', 'c1', 'Ward', true),
      entry('e2', 'c2', 'Harbour'),
      entry('e3', 'c3', 'Goodhart'),
    ],
  };

  it('leaves a hidden category out, with or without a search', () => {
    const { sections } = buildKbSidebar(kb, '', ['c2']);
    expect(sections.map((s) => [s.key, s.groups.map((g) => g.name)])).toEqual([
      ['series', ['People']],
      ['book', ['Laws']],
    ]);
    expect(buildKbSidebar(kb, 'harb', ['c2']).sections).toEqual([]);
  });

  it('drops a section whose categories are all hidden', () => {
    expect(buildKbSidebar(kb, '', ['c1']).sections.map((s) => s.key)).toEqual(['book']);
    expect(buildKbSidebar(kb, '', ['c1', 'c2', 'c3']).sections).toEqual([]);
  });

  it('does not keep an empty category that is hidden', () => {
    const empty: Kb = { categories: [category('c1', 'Characters', 'a0')], entries: [] };
    expect(buildKbSidebar(empty, '', ['c1']).sections).toEqual([]);
  });

  it('lists every category for the filter, series first, hidden ones included', () => {
    const { categories, totalEntries } = buildKbSidebar(kb, '', ['c2']);
    expect(categories.map((c) => c.name)).toEqual(['People', 'Places', 'Laws']);
    expect(totalEntries).toBe(3);
  });
});
