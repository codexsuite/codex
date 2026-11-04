import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubReferencesApi, stubSources } from '../../test-support/referenceStubs';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { refsTestIds } from './testIds';

// References below the write role (#80): sources, filters and the bibliography read; nothing is added, signed off or
// edited.
const book = allBooks(library)[0];
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };
const [risk] = stubSources;

test('References_WhenViewOnly_ShouldReadWithNoActions', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({ json: { ...headerFor(book), role: 'view', owner } }),
  );
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${book.id}/references#${risk.id}` },
  });

  await expect(page.getByTestId(refsTestIds.panel)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveText('View only');
  for (const id of [
    refsTestIds.addOpen,
    refsTestIds.addImportOpen,
    refsTestIds.flagInbox,
    refsTestIds.verdictAction,
    refsTestIds.panelMenu,
    refsTestIds.recheck,
    refsTestIds.remove,
  ]) {
    await expect(page.getByTestId(id)).toHaveCount(0);
  }
  await expect(page.getByTestId(refsTestIds.field('title'))).toBeDisabled();
  await expect(
    page.getByTestId(refsTestIds.useKind('bbbbbbbb-0000-4000-8000-000000000001')),
  ).toBeDisabled();

  // Filters and the bibliography still work.
  await page.getByTestId(refsTestIds.chip('needs_check')).click();
  await expect(page.getByTestId(refsTestIds.chip('needs_check'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await page.getByTestId(refsTestIds.viewBibliography).click();
  await expect(page.getByTestId(refsTestIds.bibliography)).toBeVisible();
});

test('References_WhenWrite_ShouldOfferTheActions', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({ json: { ...headerFor(book), role: 'write', owner } }),
  );
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${book.id}/references#${risk.id}` },
  });

  await expect(page.getByTestId(refsTestIds.addOpen)).toBeVisible();
  await expect(page.getByTestId(refsTestIds.panelMenu)).toBeVisible();
  await expect(page.getByTestId(refsTestIds.field('title'))).toBeEnabled();
});
