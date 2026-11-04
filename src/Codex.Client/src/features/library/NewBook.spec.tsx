import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { headerFor, stubShellApi } from '../../test-support/shellStubs';
import { testIds } from '../../testIds';
import { seriesTestIds } from '../series/testIds';
import { importTestIds } from './importTestIds';
import { libraryTestIds } from './testIds';

const series = library.series[0];
const newId = '0199a000-0000-7000-8000-0000000000aa';

type Mount = (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>;

async function openDialog(page: Parameters<typeof stubShellApi>[0], mount: Mount, route = '/') {
  await stubShellApi(page, library);
  await page.route(`**/api/series/${series.id}/next-number`, (r) =>
    r.fulfill({ json: { seriesId: series.id, nextNumber: 7 } }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route } });
}

test('NewBook_WhenTitleEmpty_ShouldShowInlineErrorAndSendNothing', async ({ mount, page }) => {
  await openDialog(page, mount);
  let posted = false;
  await page.route('**/api/books', (r) => {
    posted = true;
    return r.fulfill({ status: 500, json: { error: 'unexpected', code: 'internal_error' } });
  });
  await page.getByTestId(libraryTestIds.newBook).click();
  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.newBookTitle)).toBeFocused();

  await page.getByTestId(libraryTestIds.newBookCreate).click();

  await expect(page.getByTestId(libraryTestIds.newBookTitleError)).toBeVisible();
  expect(posted).toBe(false);
});

test('NewBook_WhenCancelled_ShouldCloseAndReturnFocusToOpener', async ({ mount, page }) => {
  await openDialog(page, mount);
  await page.getByTestId(libraryTestIds.newBook).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeVisible();

  await page.getByTestId(libraryTestIds.newBookCancel).click();

  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeHidden();
  await expect(page.getByTestId(libraryTestIds.newBook)).toBeFocused();
});

test('NewBook_WhenStandalone_ShouldPostAndOpenPlan', async ({ mount, page }) => {
  await openDialog(page, mount, '/?book=new');
  await page.route(`**/api/books/${newId}`, (r) =>
    r.fulfill({ json: { ...headerFor(library.standalone[0]), id: newId, title: 'Fresh' } }),
  );
  let body: unknown;
  await page.route('**/api/books', async (r) => {
    body = r.request().postDataJSON();
    await r.fulfill({
      status: 201,
      json: {
        id: newId,
        title: 'Fresh',
        subtitle: null,
        seriesId: null,
        seriesNumber: null,
        status: 'planning',
        targetWords: 80000,
      },
    });
  });
  await page.getByTestId(libraryTestIds.newBookTitle).fill('  Fresh ');

  await page.getByTestId(libraryTestIds.newBookCreate).click();

  await expect(page.getByTestId(testIds.shell.bookBar)).toBeVisible();
  expect(body).toEqual({ title: 'Fresh', targetWords: 80000 });
});

test('NewBook_WhenOpenedFromSeries_ShouldPrefillSeriesAndNextNumber', async ({ mount, page }) => {
  await openDialog(page, mount, `/#series=${series.id}`);
  await page.getByTestId(seriesTestIds.addBook).click();

  await expect(page.getByTestId(libraryTestIds.newBookSeries)).toHaveValue(series.id);
  await expect(page.getByTestId(libraryTestIds.newBookNumber)).toHaveValue('7');
});

test('NewBook_WhenNumberTaken_ShouldShowServerErrorAndStay', async ({ mount, page }) => {
  await openDialog(page, mount, `/?book=new#series=${series.id}`);
  await page.route('**/api/books', (r) =>
    r.fulfill({
      status: 409,
      json: { error: 'That book number is already taken in this series.', code: 'conflict' },
    }),
  );
  await page.getByTestId(libraryTestIds.newBookTitle).fill('Clash');
  await expect(page.getByTestId(libraryTestIds.newBookNumber)).toHaveValue('7');

  await page.getByTestId(libraryTestIds.newBookCreate).click();

  await expect(page.getByTestId(libraryTestIds.newBookError)).toContainText('already taken');
  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeVisible();
});

test('Import_WhenDocxPicked_ShouldEnableNextAndPreviewTheUploadedFile', async ({ mount, page }) => {
  await openDialog(page, mount);
  let upload = '';
  await page.route('**/api/imports/preview', (r) => {
    upload = r.request().postData() ?? '';
    return r.fulfill({
      json: {
        source: 'docx',
        bookTitle: 'Draft',
        fileCount: 1,
        words: 12,
        creates: [{ item: 'Chapters', count: 2, destination: 'Plan · chapters' }],
        skipped: [],
      },
    });
  });
  await page.getByTestId(libraryTestIds.import).click();
  await expect(page.getByTestId(libraryTestIds.importDialog)).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.importNext)).toBeDisabled();

  await page.getByTestId(libraryTestIds.importSource('docx')).click();
  await page.getByTestId(libraryTestIds.importFile).setInputFiles({
    name: 'Draft.docx',
    mimeType: 'application/octet-stream',
    buffer: Buffer.from('x'),
  });
  await expect(page.getByTestId(libraryTestIds.importFileName)).toContainText('Draft.docx');
  await page.getByTestId(libraryTestIds.importNext).click();

  await expect(page.getByTestId(libraryTestIds.importStep)).toContainText('Step 2 of 2');
  await expect(page.getByTestId(importTestIds.previewTitle)).toHaveText('Draft');
  await expect(page.getByTestId(importTestIds.run)).toBeEnabled();
  expect(upload).toContain('name="source"');
  expect(upload).toContain('filename="Draft.docx"');
});

test('Import_WhenFileIsOverTheSizeCap_ShouldRefuseAtStepTwoAndSendNothing', async ({
  mount,
  page,
}) => {
  await openDialog(page, mount);
  let posted = false;
  await page.route('**/api/imports/preview', (r) => {
    posted = true;
    return r.fulfill({ status: 500, json: { error: 'unexpected', code: 'internal_error' } });
  });
  await page.getByTestId(libraryTestIds.import).click();
  await page.getByTestId(libraryTestIds.importSource('novelcrafter')).click();
  // A 26 MB file made in the browser: pushing that many bytes through setInputFiles is what made this test slow.
  await page.getByTestId(libraryTestIds.importFile).evaluate((input: HTMLInputElement) => {
    const transfer = new DataTransfer();
    transfer.items.add(
      new File([new Uint8Array(26 * 1024 * 1024)], 'Huge.zip', { type: 'application/zip' }),
    );
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await expect(page.getByTestId(libraryTestIds.importFileName)).toContainText('Huge.zip');
  await page.getByTestId(libraryTestIds.importNext).click();

  await expect(page.getByTestId(importTestIds.selectionError)).toContainText('at most 25 MB');
  expect(posted).toBe(false);
});
