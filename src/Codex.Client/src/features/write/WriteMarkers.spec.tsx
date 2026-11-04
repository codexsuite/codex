import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import markers from '../../../../Codex.TestSupport/Fixtures/references/markers.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { proseTestIds } from '../../prose/testIds';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const [risk, check] = markers;
const body = `He said [three hundred words](ref:${risk.useId}) and moved on.\n\nThen a pause.[^](ref:${check.useId})\n`;
const unknownSource = '0197a3c0-0000-7000-8000-0000000000ff';

async function stubMarkers(page: Page) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({
      json: {
        ...manuscript,
        scenes: manuscript.scenes.map((s) => (s.sceneId === first.id ? { ...s, body } : s)),
      },
    }),
  );
  await page.route(`**/api/books/${book.id}/beats`, (route) =>
    route.fulfill({ json: { bookId: book.id, beats: [] } }),
  );
  await page.route(`**/api/books/${book.id}/drafts`, (route) =>
    route.fulfill({ json: { bookId: book.id, drafts: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({ json: { categories: [], entries: [] } }),
  );
  await page.route(`**/api/books/${book.id}/markers`, (route) =>
    route.fulfill({ json: markers.map((m) => ({ ...m, sceneId: first.id })) }),
  );
}

const open = (mount: (component: React.JSX.Element, options: object) => unknown, hash = '') =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write${hash}` } });

test('Marker_ShouldShowNumberInStatusTone', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount);

  const all = page.getByTestId(writeTestIds.anyMarker);
  await expect(all).toHaveCount(2);
  await expect(all.nth(0)).toHaveText('1');
  await expect(all.nth(0)).toHaveAttribute('data-tone', 'danger');
  await expect(all.nth(1)).toHaveAttribute('data-number', '2');
  await expect(all.nth(1)).toHaveAttribute('data-tone', 'warn');
});

test('MarkerCard_WhenClicked_ShouldShowSourceAndLinkToReferences', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount);

  await page.getByTestId(writeTestIds.anyMarker).first().click();

  await expect(page.getByTestId(writeTestIds.markerCardSource)).toHaveText(risk.sourceTitle);
  await expect(page.getByTestId(writeTestIds.markerCardStatus)).toHaveText('Copyright risk');
  await expect(page.getByTestId(writeTestIds.markerCardReason)).toHaveText('Quote over limit');
  await expect(page.getByTestId(writeTestIds.markerCardWords)).toHaveText(
    `${risk.quotedWords} of ${risk.limit} quoted words`,
  );
  await expect(page.getByTestId(writeTestIds.markerCardOpen)).toHaveAttribute(
    'href',
    `/books/${book.id}/references#${risk.sourceId}`,
  );
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(0);
});

test('MarkerCard_WhenClickedOutside_ShouldClose', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount);
  await page.getByTestId(writeTestIds.anyMarker).first().click();
  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(1);

  await page.getByTestId(writeTestIds.toolbar).click();

  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(0);
});

test('MarkerCard_WhenDeepLinked_ShouldOpenForThatSource', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount, `#ref=${check.sourceId}`);

  await expect(page.getByTestId(writeTestIds.markerCardSource)).toHaveText(check.sourceTitle);
});

test('MarkerCard_WhenSourceIsUnknown_ShouldOpenNothingAndShowNoError', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount, `#ref=${unknownSource}`);

  await expect(page.getByTestId(writeTestIds.anyMarker)).toHaveCount(2);
  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.loadError)).toHaveCount(0);
});

test('Markers_AfterASceneSave_ShouldBeReadAgain', async ({ mount, page }) => {
  await stubMarkers(page);
  let saved = false;
  await page.route(`**/api/books/${book.id}/markers`, (route) =>
    route.fulfill({ json: saved ? markers.map((m) => ({ ...m, sceneId: first.id })) : [] }),
  );
  await page.route('**/api/scenes/*/prose', (route) => {
    if (route.request().method() !== 'PUT') return route.fallback();
    saved = true;
    return route.fulfill({ status: 200, json: { version: 2 } });
  });
  await open(mount);
  await expect(page.getByTestId(writeTestIds.anyMarker)).toHaveCount(0);

  await page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor).click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' More.');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(page.getByTestId(writeTestIds.anyMarker)).toHaveCount(2);
});

test('Marker_WhenFocusedAndEnterPressed_ShouldOpenItsCard', async ({ mount, page }) => {
  await stubMarkers(page);
  await open(mount);

  await page.getByTestId(writeTestIds.anyMarker).first().focus();
  await page.keyboard.press('Enter');

  await expect(page.getByTestId(writeTestIds.markerCardSource)).toHaveText(risk.sourceTitle);
});

test('MarkerCard_WhenOpenedWithEnter_ShouldTakeFocusReachItsLinkAndGiveFocusBackOnEscape', async ({
  mount,
  page,
}) => {
  await stubMarkers(page);
  await open(mount);
  const marker = page.getByTestId(writeTestIds.anyMarker).first();
  await marker.focus();

  await page.keyboard.press('Enter');

  await expect(page.getByTestId(writeTestIds.markerCard)).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByTestId(writeTestIds.markerCardOpen)).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(writeTestIds.markerCard)).toHaveCount(0);
  await expect(marker).toBeFocused();
});
