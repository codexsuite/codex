import { expect, test } from '@playwright/experimental-ct-react';
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import labels from '../../../../Codex.TestSupport/Fixtures/structure/labels.json' with { type: 'json' };
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { planTestIds } from './testIds';

const bookId = library.standalone[0].id;
const [flashback] = labels;

async function openManager(
  mount: (c: React.JSX.Element, o: object) => Promise<unknown>,
  page: import('playwright').Page,
) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${bookId}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${bookId}/kb`, (route) => route.fulfill({ json: kb }));
  await page.route(`**/api/books/${bookId}/labels`, (route) => route.fulfill({ json: labels }));
  await page.evaluate(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${bookId}/plan` } });
  await page.getByTestId(planTestIds.labelFilter).click();
  await page.getByTestId(planTestIds.manageLabels).click();
}

test('ManageLabels_WhenRenamed_ShouldPatchTheName', async ({ mount, page }) => {
  await openManager(mount, page);
  await page.route(`**/api/labels/${flashback.id}`, (route) =>
    route.fulfill({ json: { ...flashback, name: 'Looking back' } }),
  );
  const patched = page.waitForRequest((r) => r.method() === 'PATCH');
  await page.getByTestId(planTestIds.labelRename(flashback.id)).fill('Looking back');
  await page.getByTestId(planTestIds.labelRename(flashback.id)).press('Enter');
  expect((await patched).postDataJSON()).toEqual({ name: 'Looking back', color: null });
});

test('ManageLabels_WhenDeleted_ShouldSayHowManyScenesLoseItThenDelete', async ({ mount, page }) => {
  await openManager(mount, page);
  await page.route(`**/api/labels/${flashback.id}`, (route) => route.fulfill({ status: 204 }));
  await page.getByTestId(planTestIds.labelDelete(flashback.id)).click();
  await expect(page.getByTestId(planTestIds.labelDeleteMessage)).toHaveText(
    new RegExp(
      `^Used on ${flashback.sceneCount} scenes?\\. Deleting removes it from (it|them)\\.$`,
    ),
  );
  const deleted = page.waitForRequest((r) => r.method() === 'DELETE');
  await page.getByTestId(planTestIds.labelDeleteConfirm).click();
  expect((await deleted).url()).toContain(flashback.id);
});

test('ManageLabels_WhenCancelled_ShouldDeleteNothing', async ({ mount, page }) => {
  await openManager(mount, page);
  let deleted = false;
  await page.route(`**/api/labels/${flashback.id}`, (route) => {
    deleted = true;
    return route.fulfill({ status: 204 });
  });
  await page.getByTestId(planTestIds.labelDelete(flashback.id)).click();
  await page.getByTestId(planTestIds.labelDeleteCancel).click();
  await expect(page.getByTestId(planTestIds.labelDeleteMessage)).toHaveCount(0);
  expect(deleted).toBe(false);
});

test('ManageLabels_WhenTheNameExists_ShouldShowTheServersMessage', async ({ mount, page }) => {
  await openManager(mount, page);
  await page.route(`**/api/books/${bookId}/labels`, (route) =>
    route.request().method() === 'POST'
      ? route.fulfill({
          status: 409,
          json: { error: 'A label named "Maybe cut" already exists.', code: 'conflict' },
        })
      : route.fulfill({ json: labels }),
  );
  await page.getByTestId(planTestIds.labelNewName).fill('Maybe cut');
  await page.getByTestId(planTestIds.labelNewCreate).click();
  await expect(page.getByTestId(planTestIds.labelError)).toContainText('already exists');
});
