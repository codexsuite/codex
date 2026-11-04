import type { Kb } from '@codex/core/api/kb';
import { buildKbSidebar } from '@codex/core/features/kb/kbSidebar';

// The Knowledge tab's model: the core's sidebar grouping (position order, series first, search over name + aliases),
// narrowed by the chip strip (a category or *Unused*) and joined with the entry details a row shows.

export type KbFilter =
  { kind: 'all' } | { kind: 'unused' } | { kind: 'category'; categoryId: string };

export const allFilter: KbFilter = { kind: 'all' };

export interface KbListRow {
  id: string;
  name: string;
  alwaysInContext: boolean;
  /** The first line of the summary, from the server; empty when there is none. */
  summaryLine: string;
  sceneCount: number;
}

export interface KbListGroup {
  id: string;
  name: string;
  colour: string | null;
  rows: KbListRow[];
}

export interface KbListSection {
  key: 'series' | 'book';
  seriesName: string | null;
  groups: KbListGroup[];
}

export interface KbStripChip {
  /** `all`, `unused` or the category id (the test id suffix). */
  key: string;
  label: string;
  count: number;
  colour: string | null;
  filter: KbFilter;
}

export interface KbListModel {
  sections: KbListSection[];
  chips: KbStripChip[];
  /** Every entry of the book (and its series), before search and filter. */
  totalEntries: number;
  /** Entries shown after search and filter. */
  shown: number;
  /** Every category, series ones first: what a new entry can go in. */
  categories: { id: string; name: string; colour: string | null }[];
}

export function sameFilter(a: KbFilter, b: KbFilter): boolean {
  if (a.kind !== b.kind) return false;
  return a.kind !== 'category' || (b.kind === 'category' && a.categoryId === b.categoryId);
}

export function buildKbList(kb: Kb, query: string, filter: KbFilter): KbListModel {
  const sidebar = buildKbSidebar(kb, query);
  const details = new Map(kb.entries.map((e) => [e.id, e]));

  const sections: KbListSection[] = sidebar.sections.map((section) => ({
    key: section.key,
    seriesName: section.seriesName,
    groups: section.groups
      .filter((g) => filter.kind !== 'category' || g.id === filter.categoryId)
      .map((g) => ({
        id: g.id,
        name: g.name,
        colour: g.colour,
        rows: g.entries
          .filter((e) => filter.kind !== 'unused' || e.sceneCount === 0)
          .map((e) => ({
            id: e.id,
            name: e.name,
            sceneCount: e.sceneCount,
            alwaysInContext: details.get(e.id)?.alwaysInContext ?? false,
            summaryLine: details.get(e.id)?.summaryLine ?? '',
          })),
      }))
      // A category nobody has an entry in yet keeps its heading (and `+`), as in the sidebar; *Unused* lists only what is unused.
      .filter((g) => g.rows.length > 0 || filter.kind !== 'unused'),
  }));

  const chips: KbStripChip[] = [
    { key: 'all', label: 'All', count: kb.entries.length, colour: null, filter: allFilter },
    ...sidebar.categories.map((c) => ({
      key: c.id,
      label: c.name,
      count: kb.entries.filter((e) => e.categoryId === c.id).length,
      colour: c.colour,
      filter: { kind: 'category', categoryId: c.id } as const,
    })),
    {
      key: 'unused',
      label: 'Unused',
      count: kb.entries.filter((e) => e.sceneCount === 0).length,
      colour: null,
      filter: { kind: 'unused' },
    },
  ];

  const shown = sections.reduce((n, s) => n + s.groups.reduce((m, g) => m + g.rows.length, 0), 0);
  return {
    sections: sections.filter((s) => s.groups.length > 0),
    chips,
    totalEntries: kb.entries.length,
    shown,
    categories: sidebar.categories,
  };
}
