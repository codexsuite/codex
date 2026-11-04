import { expect, test } from '@playwright/experimental-ct-react';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const bookId = library.standalone[0].id;
const [act] = outline.acts;
const [, first] = act.chapters;
const [opening] = first.scenes;

test('PlanPage_WhenDefaultView_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.evaluate(() => window.localStorage.clear());
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });

  await expect(page.getByTestId(planTestIds.page)).toBeVisible();
  await expect(page.getByTestId(planTestIds.scene(opening.id))).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('plan-default-light.png');

  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page).toHaveScreenshot('plan-default-dark.png');
});
