import type { LayoutSettings, PublicationSettings } from '@codex/core/api/settings';

/** A series book whose citation values all come from the series unless overridden. */
export function publicationSettings(
  overrides: Partial<PublicationSettings> = {},
): PublicationSettings {
  return {
    bookId: 'book-1',
    seriesName: 'Forsaken Gods',
    style: { value: 'chicago', inherited: true },
    rulesName: 'Chicago 17',
    quoteWordLimit: { value: 300, inherited: true },
    publisher: { value: 'Ashfall Press', inherited: true },
    plannedYear: { value: 2027, inherited: true },
    formats: { value: ['print', 'ebook'], inherited: true },
    printRun: { value: null, inherited: true },
    territory: { value: 'World English', inherited: true },
    ...overrides,
  };
}

export function layoutSettings(overrides: Partial<LayoutSettings> = {}): LayoutSettings {
  return {
    template: {
      name: 'Novel 6×9',
      description: null,
      isUserTemplate: false,
      pinnedVersion: '1.0',
      latestVersion: null,
      trimSize: '6 × 9 in',
      trims: ['6 × 9 in'],
    },
    riskCount: 0,
    ...overrides,
  };
}
