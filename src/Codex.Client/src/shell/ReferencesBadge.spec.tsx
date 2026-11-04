import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../AppRoutes';
import { allBooks, headerFor, stubShellApi } from '../test-support/shellStubs';
import { testIds } from '../testIds';
import { shellTestIds } from './shellTestIds';

const book = allBooks(library)[0];

async function stubBadge(page: Page, count: number | null, tone: string | null) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({
      json: { ...headerFor(book), referenceBadgeCount: count, referenceBadgeTone: tone },
    }),
  );
}

for (const screen of ['plan', 'write', 'references', 'layout', 'settings']) {
  test(`ReferencesBadge_OnEveryBookScreen_ShouldShowRisksInDanger ${screen}`, async ({
    mount,
    page,
  }) => {
    await stubBadge(page, 3, 'danger');
    await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/${screen}` } });

    const badge = page
      .getByTestId(testIds.shell.tabReferences)
      .getByTestId(shellTestIds.referencesBadge);
    await expect(badge).toHaveText('3');
    await expect(badge).toHaveAttribute('data-tone', 'danger');
    await expect(badge).toHaveCSS('background-color', 'rgb(194, 53, 59)');
  });
}

test('ReferencesBadge_WhenOnlySourcesToCheck_ShouldShowWarn', async ({ mount, page }) => {
  await stubBadge(page, 2, 'warn');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

  const badge = page.getByTestId(shellTestIds.referencesBadge);
  await expect(badge).toHaveText('2');
  await expect(badge).toHaveCSS('background-color', 'rgb(183, 112, 27)');
});

for (const [count, tone] of [
  [0, null],
  [null, null],
] as const) {
  test(`ReferencesBadge_WhenNothingToCount_ShouldBeHidden ${String(count)}`, async ({
    mount,
    page,
  }) => {
    await stubBadge(page, count, tone);
    await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/plan` } });

    await expect(page.getByTestId(testIds.shell.tabReferences)).toBeVisible();
    await expect(page.getByTestId(shellTestIds.referencesBadge)).toHaveCount(0);
  });
}
