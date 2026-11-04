import type { Kb, KbCategory, KbEntrySummary } from '@codex/core/api/kb';
import type { KbEntry } from '@codex/core/api/kbEntries';

export function kbCategory(overrides: Partial<KbCategory> = {}): KbCategory {
  return {
    id: 'category-1',
    name: 'Characters',
    tag: 'person',
    colour: null,
    position: 'a0',
    fieldDefs: [],
    fromSeries: false,
    seriesName: null,
    ...overrides,
  };
}

export function kbEntrySummary(overrides: Partial<KbEntrySummary> = {}): KbEntrySummary {
  return {
    id: 'entry-1',
    categoryId: 'category-1',
    name: 'Ana',
    aliases: [],
    sceneCount: 2,
    useCount: 3,
    fromSeries: false,
    seriesName: null,
    alwaysInContext: false,
    summaryLine: '',
    ...overrides,
  };
}

export function kb(overrides: Partial<Kb> = {}): Kb {
  return { categories: [kbCategory()], entries: [kbEntrySummary()], ...overrides };
}

export function kbEntry(overrides: Partial<KbEntry> = {}): KbEntry {
  return {
    id: 'entry-1',
    bookId: 'book-1',
    categoryId: 'category-1',
    categoryName: 'Characters',
    categoryColour: null,
    fieldDefs: [],
    name: 'Ana',
    slug: 'ana',
    summary: 'The queen who does not come.',
    body: '',
    fields: {},
    customFields: [],
    aliases: ['The Queen'],
    alwaysInContext: false,
    tags: [],
    excludeFromAutoContext: false,
    positionInCategory: 1,
    categoryEntryCount: 1,
    source: null,
    related: [],
    usedIn: [],
    timeline: [],
    createdLabel: '1 Oct',
    updatedLabel: '2h ago',
    ...overrides,
  };
}
