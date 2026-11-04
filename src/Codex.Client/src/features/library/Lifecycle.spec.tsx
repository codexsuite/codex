import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { seriesTestIds } from '../series/testIds';
import { libraryTestIds } from './testIds';

const series = library.series[0];
const book = library.standalone[0];

type Mount = (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>;
type Page = Parameters<typeof stubShellApi>[0];

async function open(page: Page, mount: Mount, route: string, data = library) {
  await stubShellApi(page, data);
  await mount(<AppRoutes />, { hooksConfig: { route } });
}

test('NewSeries_WhenNameEmpty_ShouldShowInlineErrorAndSendNothing', async ({ mount, page }) => {
  await open(page, mount, '/?newSeries=1');
  let posted = false;
  await page.route('**/api/series', (r) => {
    posted = true;
    return r.fulfill({ status: 500, json: { error: 'unexpected', code: 'internal_error' } });
  });

  await page.getByTestId(libraryTestIds.newSeriesCreate).click();

  await expect(page.getByTestId(libraryTestIds.newSeriesNameError)).toBeVisible();
  expect(posted).toBe(false);
});

test('NewSeries_WhenBooksPickedInOrder_ShouldPostNameAndIdsInThatOrder', async ({
  mount,
  page,
}) => {
  await open(page, mount, '/?newSeries=1', {
    ...library,
    standalone: [book, { ...book, id: '0199a000-0000-7000-8000-0000000000bb', title: 'Other' }],
  });
  let body: unknown;
  await page.route('**/api/series', async (r) => {
    body = r.request().postDataJSON();
    await r.fulfill({ status: 201, json: { id: 'x', name: 'Editions', bookIds: [] } });
  });
  await page.getByTestId(libraryTestIds.newSeriesName).fill(' Editions ');
  await page
    .getByTestId(libraryTestIds.newSeriesBook('0199a000-0000-7000-8000-0000000000bb'))
    .check();
  await page.getByTestId(libraryTestIds.newSeriesBook(book.id)).check();

  await page.getByTestId(libraryTestIds.newSeriesCreate).click();

  await expect(page.getByTestId(libraryTestIds.newSeriesDialog)).toBeHidden();
  expect(body).toEqual({
    name: 'Editions',
    bookIds: ['0199a000-0000-7000-8000-0000000000bb', book.id],
  });
});

test('NewSeries_WhenNameAlreadyUsed_ShouldShowANoticeButAllowIt', async ({ mount, page }) => {
  await open(page, mount, '/?newSeries=1');

  await page.getByTestId(libraryTestIds.newSeriesName).fill(series.name);

  await expect(page.getByTestId(libraryTestIds.newSeriesNotice)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.newSeriesCreate)).toBeEnabled();
});

test('DeleteBook_WhenTitleDiffers_ShouldKeepDeleteDisabled', async ({ mount, page }) => {
  const archived = { ...book, archived: true };
  await open(page, mount, '/?status=archived', { ...library, standalone: [archived] });
  await page.route(`**/api/books/${book.id}/delete-impact`, (r) =>
    r.fulfill({
      json: {
        bookId: book.id,
        title: book.title,
        archived: true,
        chapters: 3,
        scenes: 6,
        words: 1155,
        sources: 0,
      },
    }),
  );
  await page.getByTestId(libraryTestIds.bookMenu(book.id)).click();
  await page.getByTestId(libraryTestIds.deleteBookItem(book.id)).click();
  await expect(page.getByTestId(libraryTestIds.deleteBookImpact)).toContainText(
    '3 chapters, 6 scenes, 1,155 words, 0 sources',
  );

  await page.getByTestId(libraryTestIds.deleteBookTitle).fill('Not it');
  await expect(page.getByTestId(libraryTestIds.deleteBookConfirm)).toBeDisabled();
  await page.getByTestId(libraryTestIds.deleteBookTitle).fill(book.title);

  await expect(page.getByTestId(libraryTestIds.deleteBookConfirm)).toBeEnabled();
});

test('DeleteSeries_WhenConfirmed_ShouldSendDeleteAndLeaveTheSeriesScreen', async ({
  mount,
  page,
}) => {
  await open(page, mount, `/#series=${series.id}`);
  await page.route(`**/api/series/${series.id}/delete-impact`, (r) =>
    r.fulfill({
      json: {
        seriesId: series.id,
        name: series.name,
        books: 3,
        categories: [{ name: 'Spells', entries: 3 }],
        labels: ['Playtested'],
      },
    }),
  );
  let deleted = false;
  await page.route(`**/api/series/${series.id}`, (r) => {
    if (r.request().method() !== 'DELETE') return r.fallback();
    deleted = true;
    return r.fulfill({ status: 204 });
  });
  await page.getByTestId(seriesTestIds.settingsButton).click();
  await page.getByTestId(seriesTestIds.deleteButton).click();
  await expect(page.getByTestId(seriesTestIds.deleteImpact)).toContainText(
    'Its 3 books become standalone',
  );

  await page.getByTestId(seriesTestIds.deleteConfirm).click();

  await expect.poll(() => deleted).toBe(true);
});

test('RestoreSeries_WhenArchivedSeriesListed_ShouldPostRestoreAndOfferNoBookRestore', async ({
  mount,
  page,
}) => {
  const archivedSeries = {
    ...series,
    archived: true,
    books: series.books.map((b) => ({ ...b, archived: true })),
  };
  await open(page, mount, '/?status=archived', {
    ...library,
    series: [archivedSeries],
    standalone: [],
  });
  let restored = false;
  await page.route(`**/api/series/${series.id}/restore`, (r) => {
    restored = true;
    return r.fulfill({ json: { id: series.id, name: series.name, description: null } });
  });
  await expect(page.getByTestId(libraryTestIds.archivedSeries(series.id))).toContainText(
    series.name,
  );
  await expect(page.getByTestId(libraryTestIds.restoreBook(series.books[0].id))).toHaveCount(0);

  await page.getByTestId(libraryTestIds.restoreSeries(series.id)).click();

  await expect.poll(() => restored).toBe(true);
});

test('ArchiveSeries_WhenClickedInSettings_ShouldPostArchiveAndLeaveTheSeriesScreen', async ({
  mount,
  page,
}) => {
  await open(page, mount, `/#series=${series.id}`);
  let archived = false;
  await page.route(`**/api/series/${series.id}/archive`, (r) => {
    archived = true;
    return r.fulfill({ json: { id: series.id, name: series.name, description: null } });
  });
  await page.getByTestId(seriesTestIds.settingsButton).click();

  await page.getByTestId(seriesTestIds.archiveButton).click();

  await expect.poll(() => archived).toBe(true);
});
