import type { BookSettings } from '@codex/core/api/settings';

export function bookSettings(overrides: Partial<BookSettings> = {}): BookSettings {
  return {
    id: 'book-1',
    title: 'The Empty Seat',
    authorName: 'JD',
    status: 'writing',
    targetWords: 90_000,
    wordCount: 42_000,
    progressPercent: 47,
    seriesId: 'series-1',
    seriesName: 'Forsaken Gods',
    seriesNumber: 3,
    seriesOptions: [{ id: 'series-1', name: 'Forsaken Gods', bookCount: 3 }],
    seriesOrder: [],
    styleGuide: null,
    seriesGuide: null,
    archived: false,
    subtitle: null,
    description: null,
    ...overrides,
  };
}
