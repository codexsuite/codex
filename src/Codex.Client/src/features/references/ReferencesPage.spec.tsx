import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubReferencesApi, stubSources } from '../../test-support/referenceStubs';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { refsTestIds } from './testIds';

const book = allBooks(library)[0];
const route = (hash = '') => `/books/${book.id}/references${hash}`;

test('ReferencesPage_WhenLoaded_ShouldReadClearedOfCitedAndGroupRiskFirst', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await expect(page.getByTestId(refsTestIds.clearedText)).toHaveText(
    '1 of 4 cited sources cleared',
  );
  await expect(page.getByTestId(refsTestIds.gate)).toHaveAttribute('data-blocked', 'true');
  await expect(page.getByTestId(refsTestIds.row(stubSources[0].id))).toBeVisible();
  const groups = page.locator(`[data-testid^="refs-group-"]`);
  await expect(groups).toHaveText([
    'Copyright risk · 1',
    'Needs check · 2',
    'Cleared · 1',
    'Not cited · 1',
  ]);
});

test('ReferencesPage_WhenNeedsCheckChipPressed_ShouldFilterRows', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.chip('needs_check')).click();

  await expect(page.getByTestId(refsTestIds.chip('needs_check'))).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.locator('[data-testid^="refs-row-"][data-status]')).toHaveCount(2);
  await expect(page.locator('[data-testid^="refs-row-"][data-status="needs_check"]')).toHaveCount(
    2,
  );
});

test('ReferencesPage_WhenHashNamesASource_ShouldSelectItsRow', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route(`#${stubSources[2].id}`) } });

  await expect(page.getByTestId(refsTestIds.row(stubSources[2].id))).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByTestId(refsTestIds.row(stubSources[0].id))).toHaveAttribute(
    'aria-selected',
    'false',
  );
});

test('ReferencesPage_WhenRowClicked_ShouldSelectIt', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.row(stubSources[1].id)).click();

  await expect(page.getByTestId(refsTestIds.row(stubSources[1].id))).toHaveAttribute(
    'aria-selected',
    'true',
  );
});

test('ReferencesPage_WhenTheScreenNarrows_ShouldDropKindAndLicenceThenQuoted', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.setViewportSize({ width: 1200, height: 800 });
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await expect(page.getByTestId(refsTestIds.columnHead('quoted'))).toBeVisible();
  await expect(page.getByTestId(refsTestIds.columnHead('kind'))).toBeHidden();
  await expect(page.getByTestId(refsTestIds.columnHead('licence'))).toBeHidden();

  await page.setViewportSize({ width: 960, height: 800 });

  await expect(page.getByTestId(refsTestIds.columnHead('usedIn'))).toBeVisible();
  await expect(page.getByTestId(refsTestIds.columnHead('quoted'))).toBeHidden();
});

test('ReferencesPage_WhenSearched_ShouldListOnlyMatchingSources', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.search).fill('brooks');

  await expect(page.locator('[data-testid^="refs-row-"][data-status]')).toHaveCount(1);
  await expect(page.getByTestId(refsTestIds.row(stubSources[1].id))).toBeVisible();
});

test('ReferencesPage_WhenLoaded_ShouldShowTheBookStatsInTheBookBar', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await expect(page.getByTestId(refsTestIds.stats)).toHaveText(
    '5 sources · 5 citations · 342 words quoted',
  );
});

test('ReferencesPage_WhenGroupedByKind_ShouldGroupWithKindLabels', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.groupSelect).selectOption('kind');

  await expect(page.locator(`[data-testid^="refs-group-"]`)).toHaveText([
    'Book · 4',
    'Web page · 1',
  ]);
});

test('ReferencesPage_WhenBibliographyOpened_ShouldListCitedSourcesAndSwitchStyle', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.viewBibliography).click();

  const entries = page.getByTestId(refsTestIds.bibEntry);
  await expect(entries).toHaveCount(4);
  await expect(entries.filter({ hasText: 'Lean Software Development' })).toHaveCount(0);
  await expect(
    page.locator(`[data-testid="${refsTestIds.bibEntry}"][data-source-id="${stubSources[2].id}"]`),
  ).toHaveAttribute('data-status', 'risk');
  await expect(page.getByTestId(refsTestIds.bibCredit)).toHaveText(
    'Reprinted with permission of Addison-Wesley.',
  );
  await expect(entries.first()).toBeVisible();
  const chicago = await entries.first().innerText();

  await page.getByTestId(refsTestIds.bibStyle).selectOption('apa');

  await expect.poll(async () => entries.first().innerText()).not.toBe(chicago);
});

test('ReferencesPage_WhenBookSavesAnotherStyle_ShouldPreselectIt', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id, 'harvard');
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await page.getByTestId(refsTestIds.viewBibliography).click();

  await expect(page.getByTestId(refsTestIds.bibStyle)).toHaveValue('harvard');
});

test('ReferencesPage_WhenSourcesRequestFails_ShouldShowAnErrorState', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.route(`**/api/books/${book.id}/sources`, (r) =>
    r.fulfill({
      status: 500,
      json: { error: 'Something went wrong on the server.', code: 'internal_error' },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await expect(page.getByTestId(refsTestIds.loadError)).toContainText('Something went wrong');
});

test('ReferencesPage_WhenAReplyIsOverdue_ShouldShowTheChipAndTheRowReminder', async ({
  mount,
  page,
}) => {
  await stubShellApi(page, library);
  await stubReferencesApi(page, book.id);
  await page.route(`**/api/books/${book.id}/references/summary`, (route) =>
    route.fulfill({
      json: {
        cleared: 1,
        total: 5,
        risks: 1,
        needsCheck: 2,
        notCited: 1,
        badge: 1,
        remindersDue: 1,
      },
    }),
  );
  await page.route(`**/api/books/${book.id}/sources`, (route) =>
    route.fulfill({
      json: stubSources.map((source, index) =>
        index === 2 ? { ...source, reminderLabel: 'Reminder due', reminderDue: true } : source,
      ),
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: route() } });

  await expect(page.getByTestId(refsTestIds.remindersChip)).toHaveText('1 reply overdue');
  await expect(
    page.getByTestId(refsTestIds.row(stubSources[2].id)).getByTestId(refsTestIds.rowReminder),
  ).toHaveText('Reminder due');
});
