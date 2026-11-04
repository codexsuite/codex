import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import {
  stubQuoteFlagsApi,
  stubReferencesApi,
  stubSources,
} from '../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { refsTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const book = allBooks(library)[0];
const selected = stubSources[2];

test('ReferencesPage_WhenSourceSelected_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await stubQuoteFlagsApi(page, book.id);
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${book.id}/references#${selected.id}` },
  });

  await expect(page.getByTestId(refsTestIds.row(selected.id))).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByTestId(refsTestIds.clearedText)).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('references-default-light.png');

  await page.emulateMedia({ colorScheme: 'dark' });
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('references-default-dark.png');
});
