import { expect, test } from '@playwright/experimental-ct-react';
import type { Page, Route } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { editingTestIds } from '../../editing/testIds';
import { imagesTestIds } from '../../images/testIds';
import { proseTestIds } from '../../prose/testIds';
import { READY_ASSET, stubPicker } from '../../test-support/imageStubs';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import type { Figure } from './figureApi';
import { writeTestIds } from './testIds';

const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const PLACED = '0199a000-0000-7000-8000-0000000000fa';

interface Sent {
  method: string;
  url: string;
  body: unknown;
}

const figureOf = (id: string, fields: Partial<Figure> = {}): Figure => ({
  id,
  sceneId: first.id,
  assetId: READY_ASSET,
  placement: 'column',
  caption: 'The Fallen World.',
  altText: null,
  missing: false,
  webUrl: `/api/assets/${READY_ASSET}/renditions/web`,
  widthPx: 480,
  heightPx: 320,
  dpi: { effectiveDpi: 141.2, targetDpi: 300, status: 'low' },
  ...fields,
});

// Write's reads with `body` as the first scene's prose (at version 4), the picker's reads, and the figure and prose
// calls. A prose save answers version 9; a prose read answers `afterRemove` at version 11. Returns what was sent.
async function stubFigures(page: Page, body: string, afterRemove = '') {
  const sent: Sent[] = [];
  const figures = new Map<string, Figure>([[PLACED, figureOf(PLACED)]]);
  const record = (route: Route) =>
    sent.push({
      method: route.request().method(),
      url: new URL(route.request().url()).pathname,
      body: route.request().postDataJSON() as unknown,
    });
  await stubShellApi(page, library);
  await stubPicker(page, { bookId: book.id });
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
  await page.route(`**/api/books/${book.id}/kb-additions`, (route) =>
    route.fulfill({ json: { bookId: book.id, additions: [] } }),
  );
  await page.route(`**/api/books/${book.id}/kb`, (route) =>
    route.fulfill({ json: { categories: [], entries: [] } }),
  );
  await page.route('**/api/scenes/*/prose', (route) => {
    if (route.request().method() === 'GET') {
      return route.fulfill({
        json: { sceneId: first.id, body: afterRemove, version: 11, updatedAt: '' },
      });
    }
    record(route);
    return route.fulfill({ json: { version: 9 } });
  });
  await page.route('**/api/scenes/*/figures', (route) => {
    record(route);
    const request = route.request().postDataJSON() as {
      figureId: string;
      placement: string;
      caption: string | null;
      altText: string | null;
    };
    figures.set(
      request.figureId,
      figureOf(request.figureId, {
        placement: request.placement,
        caption: request.caption,
        altText: request.altText,
      }),
    );
    return route.fulfill({ json: { figureId: request.figureId, version: 10 } });
  });
  await page.route('**/api/figures/*/remove', (route) => {
    record(route);
    return route.fulfill({ json: { sceneId: first.id, version: 11 } });
  });
  await page.route('**/api/figures/*', (route) => {
    const id = new URL(route.request().url()).pathname.split('/').pop()!;
    const figure = figures.get(id);
    return figure
      ? route.fulfill({ json: figure })
      : route.fulfill({ status: 404, json: { error: 'Figure not found.', code: 'not_found' } });
  });
  return sent;
}

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);

const openWrite = (mount: (c: React.JSX.Element, o: object) => Promise<unknown>) =>
  mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });

test('InsertFigure_WhenChosenFromTheSlashMenu_ShouldSendTheBodyWithItsLineAndShowTheCard', async ({
  mount,
  page,
}) => {
  const sent = await stubFigures(page, 'The yard was empty.\n');
  await openWrite(mount);

  await editor(page).locator('p').first().click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('figure')).click();
  await expect(page.getByTestId(writeTestIds.figureDialog)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.figureDialogSubmit)).toBeDisabled();
  await page.getByTestId(imagesTestIds.option(READY_ASSET)).click();
  await page.getByTestId(writeTestIds.figurePlacement('page')).click();
  await page.getByTestId(writeTestIds.figureCaptionInput).fill('The Fallen World.');
  await page.getByTestId(writeTestIds.figureAltInput).fill('A hand-drawn map.');
  await page.getByTestId(writeTestIds.figureDialogSubmit).click();

  const posts = () => sent.filter((s) => s.url.endsWith('/figures'));
  await expect.poll(() => posts().length).toBe(1);
  const call = posts()[0]!;
  const request = call.body as {
    figureId: string;
    assetId: string;
    body: string;
    version: number;
    placement: string;
    caption: string;
    altText: string;
  };
  expect([call.url, request.assetId, request.version, request.placement]).toEqual([
    `/api/scenes/${first.id}/figures`,
    READY_ASSET,
    9,
    'page',
  ]);
  expect([request.caption, request.altText]).toEqual(['The Fallen World.', 'A hand-drawn map.']);
  expect(request.body).toContain('The yard was empty.');
  expect(request.body).toContain(`::figure{id="${request.figureId}"}`);
  await expect(page.getByTestId(writeTestIds.figure(request.figureId))).toBeVisible();
  await expect(page.getByTestId(writeTestIds.figurePlacementLabel(request.figureId))).toHaveText(
    'Full page',
  );
  await expect(page.getByTestId(writeTestIds.figureDialog)).toHaveCount(0);
});

test('InsertFigure_WhenCancelled_ShouldSendNothing', async ({ mount, page }) => {
  const sent = await stubFigures(page, 'The yard was empty.\n');
  await openWrite(mount);

  await editor(page).locator('p').first().click();
  await page.keyboard.press('Control+End');
  await page.keyboard.type(' /');
  await page.getByTestId(proseTestIds.slashItem('figure')).click();
  await page.getByTestId(imagesTestIds.dialogCancel).click();

  await expect(page.getByTestId(writeTestIds.figureDialog)).toHaveCount(0);
  expect(sent.filter((s) => s.url.endsWith('/figures'))).toHaveLength(0);
});

test('RemoveFigure_ShouldTakeTheLineOutAndUndoShouldPutItBack', async ({ mount, page }) => {
  const body = `The yard was empty.\n\n::figure{id="${PLACED}"}\n`;
  const sent = await stubFigures(page, body, 'The yard was empty.\n');
  await openWrite(mount);

  await page.getByTestId(writeTestIds.figureRemove(PLACED)).click();

  await expect(page.getByTestId(writeTestIds.figure(PLACED))).toHaveCount(0);
  await expect(page.getByTestId(editingTestIds.toast)).toContainText('Figure removed');
  await page.getByTestId(editingTestIds.toastUndo).click();

  await expect(page.getByTestId(writeTestIds.figure(PLACED))).toBeVisible();
  expect(sent.map((s) => [s.method, s.url, s.body])).toEqual([
    ['POST', `/api/figures/${PLACED}/remove`, { version: 4 }],
    ['PUT', `/api/scenes/${first.id}/prose`, { body, version: 11 }],
  ]);
});
