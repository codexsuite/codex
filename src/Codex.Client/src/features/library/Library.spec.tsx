import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubReferencesApi } from '../../test-support/referenceStubs';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { libraryTestIds } from './testIds';
import { seriesTestIds } from '../series/testIds';
import { planTestIds } from '../plan/testIds';
import { refsTestIds } from '../references/testIds';

const [velocity, hymns] = library.series[0].books;
const northbound = library.standalone[0];
const seriesId = library.series[0].id;

test('LibraryTile_WhenClicked_ShouldOpenLastScreenOrPlan', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, northbound.id);
  await page.evaluate(
    (id) => window.localStorage.setItem(`codex.last.${id}`, 'references'),
    northbound.id,
  );
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await page.getByTestId(testIds.shell.tile(northbound.id)).click();
  await expect(page.getByTestId(refsTestIds.page)).toBeVisible();
  await expect
    .poll(() =>
      page.evaluate((id) => window.localStorage.getItem(`codex.last.${id}`), northbound.id),
    )
    .toBe('references');
});

test('LibraryTile_WhenNoLastScreen_ShouldOpenPlan', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.evaluate((id) => window.localStorage.removeItem(`codex.last.${id}`), northbound.id);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await page.getByTestId(testIds.shell.tile(northbound.id)).click();
  await expect(page.getByTestId(planTestIds.page)).toBeVisible();
});

test('LibraryTile_WhenServerHintsLastScreen_ShouldOpenIt', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.evaluate((id) => window.localStorage.removeItem(`codex.last.${id}`), hymns.id);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await page.getByTestId(testIds.shell.tile(hymns.id)).click();
  // Write has a real page now: the Write tab being current says the Write route opened.
  await expect(page.getByTestId(testIds.shell.tabWrite)).toHaveAttribute('aria-current', 'page');
});

test('LibraryTile_WhenEnterPressed_ShouldOpen', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.evaluate((id) => window.localStorage.removeItem(`codex.last.${id}`), northbound.id);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const tile = page.getByTestId(testIds.shell.tile(northbound.id));
  await tile.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(planTestIds.page)).toBeVisible();
});

test('SeriesStack_ShouldShowThreeCoversAndBadge', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const stack = page.getByTestId(testIds.shell.seriesStack(seriesId));
  await expect(stack.getByTestId(libraryTestIds.stackCount)).toHaveText('2');
  // The fixture series has two books, so two covers; a longer series is capped at three.
  await expect(stack.getByTestId(libraryTestIds.cover)).toHaveCount(2);
});

test('SeriesStack_WhenSeriesHasManyBooks_ShouldCapCoversAtThree', async ({ mount, page }) => {
  const books = [velocity, hymns, { ...velocity, id: 'b3' }, { ...velocity, id: 'b4' }];
  const many = { ...library, series: [{ ...library.series[0], bookCount: 4, books }] };
  await stubShellApi(page, many);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const stack = page.getByTestId(testIds.shell.seriesStack(seriesId));
  await expect(stack.getByTestId(libraryTestIds.cover)).toHaveCount(3);
  await expect(stack.getByTestId(libraryTestIds.stackCount)).toHaveText('4');
});

test('SeriesStack_WhenClicked_ShouldOpenSeriesPage', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await page.getByTestId(testIds.shell.seriesStack(seriesId)).click();
  await expect(page.getByTestId(testIds.shell.seriesScreen)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(velocity.id))).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(hymns.id))).toBeVisible();
});

test('SeriesPage_ShouldLinkBackToLibrary', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await expect(page.getByTestId(testIds.shell.seriesScreen)).toBeVisible();
  await page.getByTestId(testIds.shell.backLink).click();
  await expect(page.getByTestId(testIds.shell.libraryPage)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();
});

test('SeriesPage_WhenUnknownSeries_ShouldShowError', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/#series=nope' } });
  await expect(page.getByTestId(testIds.shell.errorUnknownBook)).toBeVisible();
});

test('LibraryPage_WhenSearching_ShouldSendQ', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const request = page.waitForRequest((r) => r.url().includes('/api/library?q=north'));
  await page.getByTestId(testIds.shell.searchBox).fill('north');
  await request;
});

test('SeriesStack_ShouldSayBookCountInOverlineAndWordsInMeta', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const stack = page.getByTestId(testIds.shell.seriesStack(seriesId));
  await expect(stack).toContainText('Series · 2 books');
  await expect(stack).toContainText(`98.3k words · ${library.series[0].lastEditedLabel}`);
});

test('Cover_WhenBookHasAuthor_ShouldDrawItAtTheFoot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  const tile = page.getByTestId(testIds.shell.tile(northbound.id));
  await expect(tile.getByTestId(libraryTestIds.coverAuthor)).toHaveText('Joel Dickson');
});

test('Cover_WhenBookHasNoAuthor_ShouldDrawNone', async ({ mount, page }) => {
  const unsigned = { ...library, standalone: [{ ...northbound, authorName: null }] };
  await stubShellApi(page, unsigned);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(testIds.shell.tile(northbound.id))).toBeVisible();
  await expect(
    page.getByTestId(testIds.shell.tile(northbound.id)).getByTestId(libraryTestIds.coverAuthor),
  ).toHaveCount(0);
});

test('AddTile_WhenClicked_ShouldOpenNewBookDialog', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await page.getByTestId(libraryTestIds.addTile).click();
  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeVisible();
});

test('AddTile_WhenSearchActive_ShouldBeHidden', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/?q=north' } });
  await expect(page.getByTestId(testIds.shell.shelf)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.addTile)).toHaveCount(0);
});

test('SeriesPage_ShouldHaveAddBookTileAndNoCountBadgeInHeader', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/series/${seriesId}/next-number`, (r) =>
    r.fulfill({ json: { seriesId, nextNumber: 3 } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${seriesId}` } });
  await expect(page.getByTestId(testIds.shell.seriesScreen)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.stackCount)).toHaveCount(0);
  await page.getByTestId(seriesTestIds.addTile).click();
  await expect(page.getByTestId(libraryTestIds.newBookSeries)).toHaveValue(seriesId);
});
