import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const FIGURE = '0199c0de-0000-7000-8000-001200000001';
const firstScene = manuscript.scenes[0];

test('FormatMenu_WhenOpened_ShouldLinkToTheBooksLayoutTab', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

  await page.getByTestId(writeTestIds.formatButton).click();
  await expect(page.getByTestId(writeTestIds.formatPreview)).toHaveAttribute(
    'href',
    `/books/${book.id}/layout`,
  );
});

test('WritePage_WhenSceneHoldsAFigureLine_ShouldDrawTheFigureCard', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({
      json: {
        ...manuscript,
        scenes: [
          { ...firstScene, body: `${firstScene.body}\n\n::figure{id="${FIGURE}"}\n` },
          ...manuscript.scenes.slice(1),
        ],
      },
    }),
  );
  await page.route(`**/api/figures/${FIGURE}`, (route) =>
    route.fulfill({
      json: {
        id: FIGURE,
        sceneId: firstScene.sceneId,
        assetId: FIGURE,
        placement: 'column',
        caption: 'A map.',
        altText: null,
        missing: true,
        webUrl: null,
        widthPx: null,
        heightPx: null,
        dpi: null,
      },
    }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await expect(page.getByTestId(writeTestIds.figureMissing(FIGURE))).toBeVisible();
});
