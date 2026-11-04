import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import { imagesTestIds } from '../../images/testIds';
import { PICKER_BOOK, READY_ASSET, stubPicker } from '../../test-support/imageStubs';
import { FigureCard } from './FigureCard';
import type { Figure } from './figureApi';
import { writeTestIds } from './testIds';

const FIGURE = '0199c0de-0000-7000-8000-001200000001';
const ASSET = '0199c0de-0000-7000-8000-001100000001';

const figure: Figure = {
  id: FIGURE,
  sceneId: '0199c0de-0000-7000-8000-000600000010',
  assetId: ASSET,
  placement: 'column',
  caption: 'The Fallen World, as the players first see it.',
  altText: 'A hand-drawn map.',
  missing: false,
  webUrl: `/api/assets/${ASSET}/renditions/web`,
  widthPx: 480,
  heightPx: 320,
  dpi: { effectiveDpi: 74.2, targetDpi: 300, status: 'low' },
};

async function stubFigure(page: Page, reply: Partial<Figure>) {
  await page.route(`**/api/figures/${FIGURE}`, (route) =>
    route.fulfill({ json: { ...figure, ...reply } }),
  );
  await page.route('**/api/assets/*/renditions/web', (route) =>
    route.fulfill({
      contentType: 'image/png',
      body: Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAHnOcQAAAAABJRU5ErkJggg==',
        'base64',
      ),
    }),
  );
}

test('FigureCard_WhenResolutionIsLow_ShouldShowImageCaptionAndBadge', async ({ mount, page }) => {
  await stubFigure(page, {});
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);
  await expect(page.getByTestId(writeTestIds.figureImage(FIGURE))).toHaveAttribute(
    'src',
    `/api/assets/${ASSET}/renditions/web`,
  );
  await expect(page.getByTestId(writeTestIds.figureCaption(FIGURE))).toHaveText(
    figure.caption ?? '',
  );
  await expect(page.getByTestId(writeTestIds.figureLowDpi(FIGURE))).toContainText('74 dpi');
});

test('FigureCard_WhenResolutionIsEnough_ShouldShowNoWarning', async ({ mount, page }) => {
  await stubFigure(page, { dpi: { effectiveDpi: 320, targetDpi: 300, status: 'ok' } });
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);
  await expect(page.getByTestId(writeTestIds.figureDpi(FIGURE))).toHaveText('320 dpi');
  await expect(page.getByTestId(writeTestIds.figureLowDpi(FIGURE))).toHaveCount(0);
});

test('FigureCard_WhenTheAssetIsMissing_ShouldShowTheMissingState', async ({ mount, page }) => {
  await stubFigure(page, { missing: true, webUrl: null, dpi: null });
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);
  await expect(page.getByTestId(writeTestIds.figureMissing(FIGURE))).toContainText('missing');
  await expect(page.getByTestId(writeTestIds.figureImage(FIGURE))).toHaveCount(0);
});

test('FigureCard_WhenTheRenditionIsNotMadeYet_ShouldSayItIsPreparing', async ({ mount, page }) => {
  await stubFigure(page, { webUrl: null });
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);
  await expect(page.getByTestId(writeTestIds.figurePending(FIGURE))).toBeVisible();
});

test('Edit_WhenSaved_ShouldPatchOnlyWhatChangedAndShowIt', async ({ mount, page }) => {
  await stubFigure(page, {});
  await stubPicker(page);
  const patches: unknown[] = [];
  await page.route(`**/api/figures/${FIGURE}`, (route) => {
    if (route.request().method() !== 'PATCH') return route.fallback();
    patches.push(route.request().postDataJSON());
    return route.fulfill({
      json: { ...figure, assetId: READY_ASSET, placement: 'page', caption: 'The pass at dawn.' },
    });
  });
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);

  await page.getByTestId(writeTestIds.figureEdit(FIGURE)).click();
  await expect(page.getByTestId(writeTestIds.figureCaptionInput)).toHaveValue(figure.caption ?? '');
  await page.getByTestId(imagesTestIds.option(READY_ASSET)).click();
  await page.getByTestId(writeTestIds.figurePlacement('page')).click();
  await page.getByTestId(writeTestIds.figureCaptionInput).fill(' The pass at dawn. ');
  await page.getByTestId(writeTestIds.figureDialogSubmit).click();

  await expect(page.getByTestId(writeTestIds.figureDialog)).toHaveCount(0);
  expect(patches).toEqual([
    { assetId: READY_ASSET, placement: 'page', caption: 'The pass at dawn.' },
  ]);
  await expect(page.getByTestId(writeTestIds.figureCaption(FIGURE))).toHaveText(
    'The pass at dawn.',
  );
  await expect(page.getByTestId(writeTestIds.figurePlacementLabel(FIGURE))).toHaveText('Full page');
});

test('Edit_WhenTheServerRefuses_ShouldKeepTheDialogOpenWithWhy', async ({ mount, page }) => {
  await stubFigure(page, {});
  await stubPicker(page);
  await page.route(`**/api/figures/${FIGURE}`, (route) =>
    route.request().method() === 'PATCH'
      ? route.fulfill({
          status: 400,
          json: { error: 'That image is outside the book.', code: 'validation' },
        })
      : route.fallback(),
  );
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);

  await page.getByTestId(writeTestIds.figureEdit(FIGURE)).click();
  await page.getByTestId(writeTestIds.figureAltInput).fill('A new description.');
  await page.getByTestId(writeTestIds.figureDialogSubmit).click();

  await expect(page.getByTestId(writeTestIds.figureDialogError)).toHaveText(
    'That image is outside the book.',
  );
  await expect(page.getByTestId(writeTestIds.figureDialog)).toBeVisible();
});

test('Remove_WhenClicked_ShouldAskTheSceneToTakeTheLineOut', async ({ mount, page }) => {
  await stubFigure(page, {});
  let removed = 0;
  await mount(
    <FigureCard
      figureId={FIGURE}
      bookId={PICKER_BOOK}
      onRemove={() => {
        removed += 1;
      }}
    />,
  );

  await page.getByTestId(writeTestIds.figureRemove(FIGURE)).click();

  await expect.poll(() => removed).toBe(1);
});

test('FigureCard_WhenTheAssetIsMissing_ShouldStillOfferEditAndRemove', async ({ mount, page }) => {
  await stubFigure(page, { missing: true, webUrl: null, dpi: null });
  await mount(<FigureCard figureId={FIGURE} bookId={PICKER_BOOK} onRemove={() => {}} />);
  await expect(page.getByTestId(writeTestIds.figureEdit(FIGURE))).toBeVisible();
  await expect(page.getByTestId(writeTestIds.figureRemove(FIGURE))).toBeVisible();
});
