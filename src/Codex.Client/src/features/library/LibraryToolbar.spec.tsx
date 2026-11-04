import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { shellTestIds } from '../../shell/shellTestIds';
import { testIds } from '../../testIds';
import { libraryTestIds } from './testIds';

const seriesId = library.series[0].id;
const northbound = library.standalone[0];

test('Toolbar_WhenControlsChange_ShouldRequestMatchingQuery', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();

  const sorted = page.waitForRequest((r) => r.url().endsWith('/api/library?sort=title'));
  await page.getByTestId(testIds.shell.sortControl).selectOption('title');
  await sorted;

  const grouped = page.waitForRequest((r) =>
    r.url().endsWith('/api/library?sort=title&group=status'),
  );
  await page.getByTestId(testIds.shell.groupControl).selectOption('status');
  await grouped;

  const filtered = page.waitForRequest((r) =>
    r.url().endsWith('/api/library?sort=title&group=status&status=writing'),
  );
  await page.getByTestId(libraryTestIds.statusFilter).selectOption('writing');
  await filtered;
  await expect(page.getByTestId(libraryTestIds.statusFilter)).toHaveValue('writing');

  const cleared = page.waitForRequest((r) =>
    r.url().endsWith('/api/library?sort=title&group=status'),
  );
  await page.getByTestId(libraryTestIds.statusFilter).selectOption('');
  await cleared;
});

test('Toolbar_WhenOpenedWithUrlState_ShouldRestoreControls', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, {
    hooksConfig: { route: '/?sort=progress&group=none&status=draft&view=list' },
  });
  await expect(page.getByTestId(testIds.shell.sortControl)).toHaveValue('progress');
  await expect(page.getByTestId(testIds.shell.groupControl)).toHaveValue('none');
  await expect(page.getByTestId(libraryTestIds.statusFilter)).toHaveValue('draft');
  await expect(page.getByTestId(testIds.shell.listView)).toBeVisible();
});

test('List_WhenGroupedBySeries_ShouldRenderGroupRows', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?view=list' } });
  const groupRows = page.getByTestId(libraryTestIds.groupRow);
  await expect(groupRows).toHaveCount(2);
  await expect(groupRows.first()).toContainText(`${library.series[0].name} · 2`);
  await expect(groupRows.last()).toContainText('Standalone · 1');
  await expect(page.getByTestId(testIds.shell.listRow(northbound.id))).toBeVisible();
});

test('List_WhenUngrouped_ShouldHaveNoGroupRows', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?view=list&group=none' } });
  await expect(page.getByTestId(testIds.shell.listRow(northbound.id))).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.groupRow)).toHaveCount(0);
});

test('List_WhenRowClicked_ShouldOpenBook', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.evaluate((id) => window.localStorage.removeItem(`codex.last.${id}`), northbound.id);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?view=list' } });
  await page.getByTestId(testIds.shell.listRow(northbound.id)).click();
  await expect(page.getByTestId(testIds.shell.tabPlan)).toHaveAttribute('aria-current', 'page');
});

test('ViewToggle_WhenListChosen_ShouldSwitchToTable', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(testIds.shell.shelf)).toBeVisible();
  await page.getByTestId(libraryTestIds.viewList).click();
  await expect(page.getByTestId(testIds.shell.listView)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.shelf)).toHaveCount(0);
});

test('Library_WhenNothingMatches_ShouldOfferClearSearch', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/library?q=zzz', (route) =>
    route.fulfill({ json: { series: [], standalone: [] } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await page.getByTestId(testIds.shell.searchBox).fill('zzz');
  await expect(page.getByTestId(libraryTestIds.empty)).toHaveText('No books match "zzz".');
  await page.getByTestId(libraryTestIds.clearSearch).click();
  await expect(page.getByTestId(testIds.shell.searchBox)).toHaveValue('');
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();
});

test('Library_WhenApiFails_ShouldShowErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route('**/api/library**', (route) =>
    route.fulfill({ status: 409, json: { error: 'Boom.', code: 'conflict' } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(libraryTestIds.loadError)).toContainText('Boom.');
});

test('Library_WhenCoverPaletteUnknown_ShouldFailVisibly', async ({ mount, page }) => {
  const broken = {
    ...library,
    standalone: [{ ...northbound, coverPalette: 'cover-99' }],
  };
  await stubShellApi(page, broken);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(libraryTestIds.loadError)).toContainText('Unknown cover palette');
});

test('Series_WhenOpenedFromSearch_ShouldIgnoreFilters', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/?q=zzz#series=${seriesId}` } });
  await expect(page.getByTestId(testIds.shell.seriesScreen)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(library.series[0].books[0].id))).toBeVisible();
});

test('Library_WhenUrlValuesUnknown_ShouldFallBackToDefaults', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, {
    hooksConfig: { route: '/?sort=bogus&group=nope&status=gone&view=cards' },
  });
  await expect(page.getByTestId(testIds.shell.sortControl)).toHaveValue('lastEdited');
  await expect(page.getByTestId(testIds.shell.groupControl)).toHaveValue('series');
  await expect(page.getByTestId(libraryTestIds.viewGrid)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();
});

test('Library_WhenSeriesHashInvalid_ShouldOfferRecoverLink', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/#series=bad id' } });
  await expect(page.getByTestId(testIds.shell.errorBadLink)).toBeVisible();
  await page.getByTestId(shellTestIds.errorBackLink).click();
  await expect(page.getByTestId(testIds.shell.libraryPage)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.errorBadLink)).toHaveCount(0);
});

test('BookBar_WhenHeaderCoverPaletteUnknown_ShouldShowMarkerAndKeepScreen', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await page.route('**/api/books/*', (route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop();
    const book = allBooks(library).find((b) => b.id === id);
    if (!book) return route.fulfill({ status: 404, json: { error: 'x', code: 'not_found' } });
    return route.fulfill({ json: { ...headerFor(book), coverPalette: 'cover-99' } });
  });
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${northbound.id}/plan` } });
  await expect(page.getByTestId(testIds.shell.bookBar)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.unknownCover)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tabWrite)).toBeVisible();
});

test('Toolbar_ShouldBeOneRowWithCreateNovelFirstAndViewControlLast', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const create = page.getByTestId(libraryTestIds.newBook);
  await expect(create).toHaveText('Create Novel');
  await expect(page.getByTestId(libraryTestIds.import)).toHaveText('Import');
  await expect(page.getByTestId(testIds.shell.searchBox)).toHaveAttribute(
    'placeholder',
    'Search name or series…',
  );
  const centres = await Promise.all(
    [
      create,
      page.getByTestId(testIds.shell.searchBox),
      page.getByTestId(testIds.shell.sortControl),
      page.getByTestId(testIds.shell.groupControl),
      page.getByTestId(libraryTestIds.statusFilter),
      page.getByTestId(testIds.shell.viewToggle),
    ].map(async (control) => {
      const box = await control.boundingBox();
      return box ? box.y + box.height / 2 : -1;
    }),
  );
  // One row: every control is centred on the first one's line (a wrapped row would sit 30px or more lower).
  for (const centre of centres) expect(Math.abs(centre - centres[0])).toBeLessThan(4);
  const create$ = await create.boundingBox();
  const view$ = await page.getByTestId(testIds.shell.viewToggle).boundingBox();
  expect(view$!.x).toBeGreaterThan(create$!.x);
});

test('Toolbar_WhenOnSeriesScreen_ShouldKeepTheLibraryControls', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await expect(page.getByTestId(libraryTestIds.newBook)).toHaveText('Create Novel');
  await expect(page.getByTestId(testIds.shell.sortControl)).toBeVisible();
  await expect(page.getByTestId(shellTestIds.paletteButton)).toBeVisible();
  await expect(page.getByTestId(shellTestIds.themeToggle)).toHaveCount(0);
});

test('ViewToggle_WhenListChosenOnSeriesScreen_ShouldListTheSeriesBooks', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await page.getByTestId(libraryTestIds.viewList).click();
  await expect(page.getByTestId(testIds.shell.listView)).toBeVisible();
  await expect(
    page.getByTestId(testIds.shell.listRow(library.series[0].books[0].id)),
  ).toBeVisible();
  await expect(page.getByTestId(testIds.shell.listRow(northbound.id))).toHaveCount(0);
});
