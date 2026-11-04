import type { Page } from 'playwright';
import currentUser from '../../../Codex.TestSupport/Fixtures/current-user.json' with { type: 'json' };
import type { BookHeader, Library, LibraryBook } from '@codex/core/api/types';

// page.route stubs for the two shell endpoints, driven by the S0 DTO fixtures. Component tests only.

export function headerFor(book: LibraryBook): BookHeader {
  return {
    id: book.id,
    title: book.title,
    subtitle: book.subtitle,
    authorName: 'Joel Dickson',
    seriesId: book.seriesId,
    seriesName: book.seriesName,
    seriesNumber: book.seriesNumber,
    status: book.status,
    coverPalette: book.coverPalette,
    coverInitials: book.coverInitials,
    wordCount: book.wordCount,
    targetWords: book.targetWords,
    scope: [{ id: book.id, kind: 'book', title: book.title }],
    referenceBadgeCount: null,
    referenceBadgeTone: null,
    lastEditedAt: book.lastEditedAt,
    lastEditedLabel: book.lastEditedLabel,
    coverUrl: book.coverUrl,
    role: book.role,
    owner: book.owner,
  };
}

export function allBooks(library: Library): LibraryBook[] {
  return [...library.series.flatMap((s) => s.books), ...library.standalone];
}

/**
 * Serves `GET /api/library`, `GET /api/books/{id}` (404 ApiError for ids not in the library) and the avatar's
 * `GET /api/auth/me` (the current-user fixture). Also answers the
 * two requests every book workspace makes on its own, so none reaches the component-test dev server (which, under
 * CI load, answered them slowly enough to hold up the screen's own request): the realtime hub (`/hubs/**`, refused,
 * so the subscription backs off) and the Plan import banner (`GET /api/books/{id}/import`, no import). A test that
 * needs either registers its own route after this one; the later route wins. Any other `/api/` request no route
 * answers (side panels such as the KB's `GET /api/books/{id}/kb`) gets an immediate 404 ApiError, the status the dev
 * server gave it, from a context route: Playwright tries page routes first, so whatever the test registers, before
 * or after this call, still wins.
 */
export async function stubShellApi(page: Page, library: Library) {
  await page.context().route('**/api/**', (route) =>
    route.fulfill({
      status: 404,
      json: { error: 'Not stubbed in this test.', code: 'not_found' },
    }),
  );
  await page.route('**/hubs/**', (route) => route.fulfill({ status: 404, body: '' }));
  await page.route('**/api/books/*/import', (route) =>
    route.fulfill({ json: { banner: null, canUndo: false } }),
  );
  await page.route('**/api/library**', (route) => route.fulfill({ json: library }));
  await page.route('**/api/auth/me', (route) => route.fulfill({ json: currentUser }));
  await page.route('**/api/books/*', (route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop();
    const book = allBooks(library).find((b) => b.id === id);
    return book
      ? route.fulfill({ json: headerFor(book) })
      : route.fulfill({
          status: 404,
          json: { error: `Book ${id} was not found.`, code: 'not_found' },
        });
  });
}
