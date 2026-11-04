import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { proseTestIds } from '../../prose/testIds';
import { stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { seriesTestIds } from './testIds';

const series = library.series[0];
const [first, second] = series.books;

async function openSettings(
  page: Parameters<typeof stubShellApi>[0],
  mount: (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>,
) {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${series.id}` } });
  await page.getByTestId(seriesTestIds.settingsButton).click();
  await expect(page.getByTestId(seriesTestIds.dialog)).toBeVisible();
}

test('SeriesPage_ShouldShowTotalsBreadcrumbAndBooksInOrder', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: `/#series=${series.id}` } });
  await expect(page.getByTestId(seriesTestIds.breadcrumb)).toContainText(series.name);
  await expect(page.getByTestId(seriesTestIds.statBooks)).toContainText(String(series.bookCount));
  await expect(page.getByTestId(seriesTestIds.statWords)).toContainText(
    series.wordCount.toLocaleString('en-US'),
  );
  await expect(page.getByTestId(seriesTestIds.statPublished)).toContainText(
    String(series.publishedCount),
  );
  await expect(page.getByTestId(testIds.shell.tile(first.id))).toBeVisible();
  await expect(page.getByTestId(seriesTestIds.addBook)).toBeVisible();
});

test('SeriesSettings_WhenAltArrowUp_ShouldRenumberAndSendNewOrder', async ({ mount, page }) => {
  await openSettings(page, mount);
  let body: unknown;
  await page.route(`**/api/series/${series.id}/order`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({
      json: {
        seriesId: series.id,
        books: [
          { id: second.id, seriesNumber: 1 },
          { id: first.id, seriesNumber: 2 },
        ],
      },
    });
  });

  const grip = page.getByTestId(seriesTestIds.grip(second.id));
  await grip.focus();
  await page.keyboard.press('Alt+ArrowUp');

  await expect(page.getByTestId(seriesTestIds.orderNumber(second.id))).toHaveText('001');
  await expect(page.getByTestId(seriesTestIds.orderNumber(first.id))).toHaveText('002');
  await expect(grip).toBeFocused();
  await page.getByTestId(seriesTestIds.save).click();
  await expect(page.getByTestId(seriesTestIds.dialog)).toBeHidden();
  expect(body).toEqual({ bookIds: [second.id, first.id] });
});

test('SeriesSettings_WhenDescriptionToggledOnly_ShouldSendNothing', async ({ mount, page }) => {
  await openSettings(page, mount);
  let sent = false;
  await page.route('**/api/series/**', (route) => {
    sent = true;
    return route.abort();
  });

  const source = page.getByTestId(proseTestIds.sourceEditor('series-settings'));
  await expect(source).toHaveText(series.description ?? '');
  await page.getByTestId(proseTestIds.modeRich('series-settings')).click();
  await page.getByTestId(proseTestIds.modeSource('series-settings')).click();
  await page.getByTestId(seriesTestIds.save).click();

  await expect(page.getByTestId(seriesTestIds.dialog)).toBeHidden();
  expect(sent).toBe(false);
});

test('SeriesSettings_WhenDescriptionTyped_ShouldPatchItWithTheName', async ({ mount, page }) => {
  await openSettings(page, mount);
  let body: unknown;
  await page.route(`**/api/series/${series.id}`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ json: { ...series, description: 'Notes.' } });
  });

  const source = page.getByTestId(proseTestIds.sourceEditor('series-settings'));
  await source.click();
  await page.keyboard.press('Control+A');
  await page.keyboard.type('Notes.');
  await page.getByTestId(seriesTestIds.save).click();

  await expect(page.getByTestId(seriesTestIds.dialog)).toBeHidden();
  expect(body).toEqual({ name: series.name, description: 'Notes.' });
});

test('SeriesSettings_WhenNameBlank_ShouldShowValidationAndSendNothing', async ({ mount, page }) => {
  await openSettings(page, mount);
  let sent = false;
  await page.route('**/api/series/**', (route) => {
    sent = true;
    return route.abort();
  });

  await page.getByTestId(seriesTestIds.nameInput).fill('   ');
  await page.getByTestId(seriesTestIds.save).click();

  await expect(page.getByTestId(seriesTestIds.nameError)).toBeVisible();
  await expect(page.getByTestId(seriesTestIds.dialog)).toBeVisible();
  expect(sent).toBe(false);
});

test('SeriesSettings_WhenSaveFails_ShouldShowServerMessageAndStayOpen', async ({ mount, page }) => {
  await openSettings(page, mount);
  await page.route(`**/api/series/${series.id}`, (route) =>
    route.fulfill({
      status: 400,
      json: { code: 'validation', error: 'A series needs a name.' },
    }),
  );

  await page.getByTestId(seriesTestIds.nameInput).fill('Renamed');
  await page.getByTestId(seriesTestIds.save).click();

  await expect(page.getByTestId(seriesTestIds.saveError)).toHaveText('A series needs a name.');
  await expect(page.getByTestId(seriesTestIds.dialog)).toBeVisible();
});

test('SeriesSettings_WhenEscape_ShouldCloseAndReturnFocusToOpener', async ({ mount, page }) => {
  await openSettings(page, mount);

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(seriesTestIds.dialog)).toBeHidden();
  await expect(page.getByTestId(seriesTestIds.settingsButton)).toBeFocused();
});
