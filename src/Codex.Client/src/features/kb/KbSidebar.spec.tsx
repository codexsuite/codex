import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import entry from '../../../../Codex.TestSupport/Fixtures/kb/entry.json' with { type: 'json' };
import kb from '../../../../Codex.TestSupport/Fixtures/kb/kb.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { HashProbe } from '../../test-support/HashProbe';
import { stubShellApi } from '../../test-support/shellStubs';
import { shellTestIds } from '../../shell/shellTestIds';
import { testIds } from '../../testIds';
import { kbTestIds } from './testIds';

const hashProbeId = 'test-hash-probe';
const hymns = library.series[0].books[1].id;
const [ward, harbour] = kb.entries;

test('KbSidebar_ShouldGroupSeriesEntriesAndCountScenes', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan` } });
  const series = page.getByTestId(kbTestIds.section('series'));
  await expect(series).toContainText('From The Tide Cycle');
  await expect(series.getByTestId(testIds.shell.kbItem(ward.id))).toContainText('Captain Ward');
  await expect(page.getByTestId(kbTestIds.count(ward.id))).toHaveText('3');
  await expect(
    page.getByTestId(kbTestIds.section('book')).getByTestId(testIds.shell.kbItem(harbour.id)),
  ).toBeVisible();
  await expect(page.getByTestId(kbTestIds.category(kb.categories[0].id))).toContainText('People');
});

test('KbSidebar_Search_ShouldMatchNameAndAlias', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan` } });
  await page.getByTestId(kbTestIds.search).fill('the captain');
  await expect(page.getByTestId(testIds.shell.kbItem(ward.id))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.kbItem(harbour.id))).toHaveCount(0);
  await page.getByTestId(kbTestIds.search).fill('zzz');
  await expect(page.getByTestId(kbTestIds.noMatch)).toBeVisible();
});

test('KbSidebar_Click_ShouldHighlightOpenTheWindowAndKeepOtherKeys', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${hymns}/plan#chapter=2` } },
  );
  await page.getByTestId(testIds.shell.kbItem(harbour.id)).click();
  await expect(page.getByTestId(testIds.shell.kbItem(harbour.id))).toHaveAttribute(
    'aria-current',
    'true',
  );
  await expect(page.getByTestId(hashProbeId)).toHaveText(
    `#chapter=2&kb=${harbour.id}&entry=${harbour.id}`,
  );
  await page.getByTestId(testIds.shell.kbItem(harbour.id)).click();
  await expect(page.getByTestId(hashProbeId)).toHaveText(`#chapter=2&kb=${harbour.id}`);
});

test('KbSidebar_WhenKbNotFound_ShouldShowErrorAndKeepPage', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) =>
    route.fulfill({ status: 404, json: { error: 'Book was not found.', code: 'not_found' } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${hymns}/plan` } });
  await expect(page.getByTestId(kbTestIds.error)).toBeVisible();
  await expect(page.getByTestId(shellTestIds.kbEmpty)).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.bookBar)).toBeVisible();
});

test('EntryWindow_WhenEntryInHash_ShouldOpenAndCloseKeepingOtherKeys', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*/kb', (route) => route.fulfill({ json: kb }));
  await page.route('**/api/kb/entries/*', (route) =>
    route.fulfill({ json: { ...entry, id: ward.id, name: ward.name } }),
  );
  await mount(
    <>
      <AppRoutes />
      <HashProbe />
    </>,
    { hooksConfig: { route: `/books/${hymns}/plan#kb=${ward.id}&entry=${ward.id}` } },
  );
  await expect(page.getByTestId(kbTestIds.entryWindowTitle)).toHaveText('Captain Ward');
  await page.getByTestId(kbTestIds.entryWindowClose).click();
  await expect(page.getByTestId(kbTestIds.entryWindow)).toHaveCount(0);
  await expect(page.getByTestId(hashProbeId)).toHaveText(`#kb=${ward.id}`);
});
