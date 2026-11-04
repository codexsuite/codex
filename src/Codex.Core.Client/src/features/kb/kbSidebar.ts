import type { Kb } from '../../api/kb';

// The Knowledge base list's model: categories and their entries, grouped for the sidebar (web) and the KB screen.

export interface KbSidebarEntry {
  id: string;
  name: string;
  /** Live scenes of this book that use the entry. */
  sceneCount: number;
}

export interface KbSidebarGroup {
  id: string;
  name: string;
  /** `category-*` colour token name, or null. */
  colour: string | null;
  entries: KbSidebarEntry[];
}

/** A category as the filter lists it. */
export interface KbSidebarCategory {
  id: string;
  name: string;
  /** `category-*` colour token name, or null. */
  colour: string | null;
}

export interface KbSidebarSection {
  /** `series` (entries shared by the book's series) or `book`. */
  key: 'series' | 'book';
  /** Series name for the `series` section. */
  seriesName: string | null;
  groups: KbSidebarGroup[];
}

export interface KbSidebarModel {
  sections: KbSidebarSection[];
  totalEntries: number;
  /** Every category, series ones first, for the filter; hidden ones included. */
  categories: KbSidebarCategory[];
}

const byPosition = (a: { position: string }, b: { position: string }) =>
  a.position < b.position ? -1 : a.position > b.position ? 1 : 0;

const matches = (name: string, aliases: string[], q: string) =>
  name.toLowerCase().includes(q) || aliases.some((a) => a.toLowerCase().includes(q));

/**
 * Categories and entries in the server's position order, series entries under `From <series>`; search over name +
 * aliases. A category in `hiddenCategories` (the sidebar's filter) is left out of both sections.
 */
export function buildKbSidebar(
  kb: Kb,
  query: string,
  hiddenCategories: readonly string[] = [],
): KbSidebarModel {
  const q = query.trim().toLowerCase();
  const sorted = [...kb.categories].sort(byPosition);
  const categories = sorted.filter((c) => !hiddenCategories.includes(c.id));
  const visible = kb.entries.filter((e) => q === '' || matches(e.name, e.aliases, q));
  const seriesName =
    kb.entries.find((e) => e.fromSeries)?.seriesName ??
    kb.categories.find((c) => c.fromSeries)?.seriesName ??
    null;

  // A category nobody has an entry in yet is listed, unfiltered by search, so the sidebar still shows it: under
  // `From <series>` when only the series has it, otherwise with the book's own.
  const hasEntries = (categoryId: string) => kb.entries.some((e) => e.categoryId === categoryId);
  const groupsFor = (fromSeries: boolean): KbSidebarGroup[] =>
    categories.flatMap((c) => {
      const entries = visible
        .filter((e) => e.categoryId === c.id && e.fromSeries === fromSeries)
        .map((e) => ({ id: e.id, name: e.name, sceneCount: e.sceneCount }));
      const keepEmpty = c.fromSeries === fromSeries && q === '' && !hasEntries(c.id);
      return entries.length === 0 && !keepEmpty
        ? []
        : [{ id: c.id, name: c.name, colour: c.colour, entries }];
    });

  const sections: KbSidebarSection[] = [];
  const series = groupsFor(true);
  if (series.length > 0) sections.push({ key: 'series', seriesName, groups: series });
  const own = groupsFor(false);
  if (own.length > 0) sections.push({ key: 'book', seriesName: null, groups: own });
  const listed = [...sorted.filter((c) => c.fromSeries), ...sorted.filter((c) => !c.fromSeries)];
  return {
    sections,
    totalEntries: kb.entries.length,
    categories: listed.map((c) => ({ id: c.id, name: c.name, colour: c.colour })),
  };
}
