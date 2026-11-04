import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const [velocity] = library.series[0].books;
const seriesId = library.series[0].id;
const northbound = library.standalone[0];

type Page = Parameters<Parameters<typeof test>[2]>[0]['page'];

async function expectBothThemes(page: Page, name: string) {
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot(`${name}-light.png`);
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot(`${name}-dark.png`);
}

test('LibraryPage_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.emulateMedia({ colorScheme: 'light' });
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(testIds.shell.libraryPage)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.seriesStack(seriesId))).toBeVisible();
  await expectBothThemes(page, 'library-default');
});

test('SeriesPage_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.emulateMedia({ colorScheme: 'light' });
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await expect(page.getByTestId(testIds.shell.seriesScreen)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(velocity.id))).toBeVisible();
  await expectBothThemes(page, 'library-series');
});
