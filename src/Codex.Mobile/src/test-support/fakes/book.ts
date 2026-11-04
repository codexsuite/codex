import type { BookHeader } from '@codex/core/api/types';

export function bookHeader(overrides: Partial<BookHeader> = {}): BookHeader {
  return {
    id: 'book-1',
    title: 'The Empty Seat',
    subtitle: null,
    authorName: 'JD',
    seriesId: 'series-1',
    seriesName: 'Forsaken Gods',
    seriesNumber: 3,
    status: 'writing',
    coverPalette: 'cover-3',
    coverInitials: 'ES',
    wordCount: 42_000,
    targetWords: 90_000,
    scope: [],
    referenceBadgeCount: null,
    referenceBadgeTone: null,
    lastEditedAt: '2026-10-20T09:00:00Z',
    lastEditedLabel: '2h ago',
    coverUrl: null,
    role: 'owner',
    owner: { id: 'user-jd', displayName: 'JD' },
    ...overrides,
  };
}
