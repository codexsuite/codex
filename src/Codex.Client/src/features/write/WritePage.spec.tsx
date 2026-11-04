import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const notFound = { error: 'Book not found.', code: 'not_found' };

test('WritePage_WhenManuscriptIs404_ShouldShowTheErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ status: 404, json: notFound }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const error = page.getByTestId(writeTestIds.loadError);
  await expect(error).toBeVisible();
  await expect(error).toContainText('Book not found.');
  await expect(page.getByTestId(writeTestIds.manuscript)).toHaveCount(0);
});

test('WritePage_WhenOutlineIs404_ShouldShowTheErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) =>
    route.fulfill({ status: 404, json: notFound }),
  );
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.loadError)).toBeVisible();
});

test('WritePage_WhenBookHasNoChapters_ShouldSaySo', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) =>
    route.fulfill({
      json: { bookId: book.id, acts: [{ ...outline.acts[0], chapters: [] }] },
    }),
  );
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: { bookId: book.id, scenes: [] } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await expect(page.getByTestId(writeTestIds.empty)).toBeVisible();
});

test('WritePage_WhenOutlineLoaded_ShouldDrawChaptersAndLinkMissingTargets', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({
      json: {
        bookId: book.id,
        scenes: outline.acts
          .flatMap((a) => a.chapters)
          .flatMap((c) => c.scenes)
          .map((s) => ({
            sceneId: s.id,
            body: 'See [Gone](chapter:0199a000-0000-7000-8000-00000000ffff) and [Old name](chapter:0199a000-0000-7000-8000-0000000000c1).\n',
            version: 1,
          })),
      },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  const refs = page.getByTestId(writeTestIds.chapterRef);
  await expect(refs.first()).toHaveText('Missing chapter');
  await expect(refs.nth(1)).toHaveText('§ Chapter 1');
  await expect(refs.first()).toHaveAttribute('data-missing', 'true');
  await expect(refs.nth(1)).toHaveAttribute('data-missing', 'false');
});
