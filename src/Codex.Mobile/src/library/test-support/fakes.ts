import type { Me } from '@codex/core/api/auth';
import type { Library } from '@codex/core/api/types';
import { libraryBook, librarySeries } from '~/test-support/fakes';

export function me(overrides: Partial<Me> = {}): Me {
  return {
    id: 'user-jd',
    email: 'jd@codex.local',
    displayName: 'Joel Dickson',
    initials: 'JD',
    isAdmin: false,
    mustChangePassword: false,
    ...overrides,
  };
}

/** Two series books (Forsaken Gods 1 and 2), one in another series and one standalone, edited in a known order. */
export function shelf(): Library {
  return {
    series: [
      librarySeries({
        id: 'series-fg',
        name: 'Forsaken Gods',
        books: [
          libraryBook({
            id: 'fg-1',
            title: 'The Empty Seat',
            seriesId: 'series-fg',
            seriesName: 'Forsaken Gods',
            seriesNumber: 1,
            wordCount: 98_400,
            targetWords: 120_000,
            progressPct: 82,
            lastEditedLabel: 'Yesterday',
            lastEditedAt: '2026-10-22T09:00:00Z',
            status: 'revising',
          }),
          libraryBook({
            id: 'fg-2',
            title: 'Hollow Throne',
            seriesId: 'series-fg',
            seriesName: 'Forsaken Gods',
            seriesNumber: 2,
            lastEditedLabel: '3d ago',
            lastEditedAt: '2026-10-20T09:00:00Z',
          }),
        ],
      }),
      librarySeries({
        id: 'series-ns',
        name: 'Night Shift',
        books: [
          libraryBook({
            id: 'ns-1',
            title: 'Graveyard Hours',
            seriesId: 'series-ns',
            seriesName: 'Night Shift',
            seriesNumber: 1,
            lastEditedLabel: '5d ago',
            lastEditedAt: '2026-10-18T09:00:00Z',
          }),
        ],
      }),
    ],
    standalone: [
      libraryBook({
        id: 'solo-1',
        title: 'Northbound',
        targetWords: null,
        progressPct: null,
        wordCount: 850,
        lastEditedLabel: '2h ago',
        lastEditedAt: '2026-10-23T07:00:00Z',
        status: 'needs_check',
      }),
    ],
  };
}
