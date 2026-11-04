import { expect, test } from '@playwright/experimental-ct-react';
import { ImagePickerHarness } from '../test-support/ImagePickerHarness';
import { pickerHarnessValue } from '../test-support/imagePickerHarnessIds';
import {
  PICKER_BOOK,
  PREPARING_ASSET,
  READY_ASSET,
  UPLOADED_ASSET,
  pngFile,
  stubPicker,
} from '../test-support/imageStubs';
import { imagesTestIds } from './testIds';

test('Picker_WhenOpened_ShouldListImagesWithStatusSizeAndDpiForTheBooksLayout', async ({
  mount,
  page,
}) => {
  const calls = await stubPicker(page);
  await mount(<ImagePickerHarness bookId={PICKER_BOOK} placement="column" />);

  await expect(page.getByTestId(imagesTestIds.option(READY_ASSET))).toContainText(
    'Fallen World.png',
  );
  await expect(page.getByTestId(imagesTestIds.option(READY_ASSET))).toContainText('480×320');
  await expect(page.getByTestId(imagesTestIds.optionDpi(READY_ASSET))).toHaveAttribute(
    'data-status',
    'low',
  );
  await expect(page.getByTestId(imagesTestIds.optionDpi(READY_ASSET))).toContainText('141 dpi');
  await expect(page.getByTestId(imagesTestIds.optionStatus(PREPARING_ASSET))).toHaveText(
    'Preparing…',
  );
  // The dpi is judged for the book's trim and its template's columns, at the dialog's placement.
  expect(calls.lists[0]).toBe('?trim=letter&columns=2&placement=column');
});

test('Picker_WhenAnImageIsChosen_ShouldSelectItAndExplainItsDpi', async ({ mount, page }) => {
  await stubPicker(page);
  await mount(<ImagePickerHarness bookId={PICKER_BOOK} placement="column" />);

  await page.getByTestId(imagesTestIds.option(READY_ASSET)).click();

  await expect(page.getByTestId(pickerHarnessValue)).toHaveText(READY_ASSET);
  await expect(page.getByTestId(imagesTestIds.option(READY_ASSET))).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await expect(page.getByTestId(imagesTestIds.selectedDpi)).toContainText(
    'Low resolution: 141 dpi at column (3.40 in wide). Print needs 300 dpi, about 1020 px wide.',
  );
});

test('Picker_WhenTheBookHasNoImages_ShouldSaySoAndOfferUpload', async ({ mount, page }) => {
  await stubPicker(page, { list: [] });
  await mount(<ImagePickerHarness bookId={PICKER_BOOK} />);

  await expect(page.getByTestId(imagesTestIds.empty)).toBeVisible();
  await expect(page.getByTestId(imagesTestIds.upload)).toBeEnabled();
});

test('Upload_WhenAnImageIsUploaded_ShouldAddAndSelectIt', async ({ mount, page }) => {
  const calls = await stubPicker(page);
  await mount(<ImagePickerHarness bookId={PICKER_BOOK} initial={READY_ASSET} />);
  await expect(page.getByTestId(imagesTestIds.option(READY_ASSET))).toBeVisible();

  await page.getByTestId(imagesTestIds.uploadInput).setInputFiles(pngFile);

  await expect(page.getByTestId(pickerHarnessValue)).toHaveText(UPLOADED_ASSET);
  await expect(page.getByTestId(imagesTestIds.option(UPLOADED_ASSET))).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await expect(page.getByTestId(imagesTestIds.optionStatus(UPLOADED_ASSET))).toHaveText(
    'Preparing…',
  );
  expect(calls.uploads).toBe(1);
});

test('Upload_WhenTheServerRefuses_ShouldShowWhyAndKeepTheSelection', async ({ mount, page }) => {
  await stubPicker(page, {
    upload: { status: 400, json: { error: 'Images can be at most 25 MB.', code: 'validation' } },
  });
  await mount(<ImagePickerHarness bookId={PICKER_BOOK} initial={READY_ASSET} />);

  await page.getByTestId(imagesTestIds.uploadInput).setInputFiles(pngFile);

  await expect(page.getByTestId(imagesTestIds.uploadError)).toHaveText(
    'Images can be at most 25 MB.',
  );
  await expect(page.getByTestId(pickerHarnessValue)).toHaveText(READY_ASSET);
});
