import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { kbTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const book = allBooks(library)[0];
const related = entry.related[0];
const base = {
  aliases: [] as string[],
  sceneCount: 1,
  useCount: 1,
  fromSeries: false,
  seriesName: null,
  alwaysInContext: false,
};
const kb = {
  categories: [
    {
      id: entry.categoryId,
      name: entry.categoryName,
      colour: entry.categoryColour,
      fieldDefs: entry.fieldDefs,
      fromSeries: false,
    },
  ],
  entries: [
    { ...base, id: related.id, categoryId: entry.categoryId, name: related.name },
    {
      ...base,
      id: entry.id,
      categoryId: entry.categoryId,
      name: entry.name,
      aliases: entry.aliases,
      sceneCount: 2,
      useCount: 2,
    },
  ],
};

async function stubWorkspace(page: Page) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) => route.fulfill({ json: entry }));
}

test('CodexEntryWindow_WhenOpenedFromSidebar_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubWorkspace(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await page.getByTestId(testIds.shell.kbItem(entry.id)).click();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toBeVisible();
  await expect(page.getByTestId(kbTestIds.entryName)).toHaveValue(entry.name);
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('codex-entry-light.png');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page).toHaveScreenshot('codex-entry-dark.png');
});
