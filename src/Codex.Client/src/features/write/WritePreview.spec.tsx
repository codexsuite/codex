import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import type { LayoutPreview, LayoutState } from '@codex/core/api/layout';
import { AppRoutes } from '../../AppRoutes';
import { proseTestIds } from '../../prose/testIds';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { previewStorageKey } from './previewStore';
import { writeTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const book = allBooks(library)[0];
const chapter = outline.acts.flatMap((a) => a.chapters).find((c) => c.scenes.length > 0)!;
const [scene1, scene2] = chapter.scenes;
// The first page block of scene 2 carries a test id of its own (test data, not app markup).
const SCENE_2_START = 'stub-scene-2-start';

const layout: LayoutState = {
  template: 'novel-6x9',
  trim: '6x9',
  templates: [
    {
      slug: 'novel-6x9',
      name: 'Novel (6 x 9)',
      version: '1.0.0',
      engine: 'tectonic',
      trims: ['6x9'],
      columns: 1,
      hasPreviewCss: false,
      notices: [],
      options: [],
    },
  ],
};

const paragraphs = (count: number, text: string) =>
  Array.from(
    { length: count },
    (_, i) =>
      `<p>${text} ${i + 1}. The road ran on past the mill and the river, and nobody on it looked back at the town.</p>`,
  ).join('');

const marker = (sceneId: string, label: string) =>
  `<div class="codex-scene" data-scene="${sceneId}" data-chapter="${chapter.id}" data-label="${label}"></div>`;

const previewOf = (extra = ''): LayoutPreview => ({
  html:
    `<h2 id="ch-${chapter.id}">${chapter.title}</h2>` +
    marker(scene1.id, 'Ch 1 · Sc 1') +
    `<p>Guyon crossed the yard.${extra}</p>` +
    paragraphs(40, 'Yard') +
    marker(scene2.id, 'Ch 1 · Sc 2') +
    `<p data-testid="${SCENE_2_START}">The guide was waiting by the gate.</p>` +
    paragraphs(40, 'Gate'),
  css: null,
  template: 'novel-6x9',
  templateName: 'Novel (6 x 9)',
  templateVersion: '1.0.0',
  trim: '6x9',
  options: {},
  notices: [],
  flags: [],
  figures: [],
  contentHash: 'h',
  renderedAt: '2026-10-21T00:00:00Z',
});

// Long scenes, so the prose can scroll the second one to the top.
const longDay = Array.from({ length: 30 }, () => 'A long day.').join('\n\n');
const longManuscript = {
  ...manuscript,
  scenes: manuscript.scenes.map((s) => ({ ...s, body: `${s.body}\n\n${longDay}` })),
};

async function stubWrite(
  page: Page,
  preview: (call: number) => { status?: number; json: unknown } = () => ({ json: previewOf() }),
) {
  const requests: unknown[] = [];
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: longManuscript }),
  );
  await page.route(`**/api/books/${book.id}/layout`, (route) => route.fulfill({ json: layout }));
  await page.route(`**/api/books/${book.id}/layout/preview`, (route) => {
    requests.push(route.request().postDataJSON());
    return route.fulfill(preview(requests.length));
  });
  await page.route('**/api/scenes/*/prose', (route) => {
    const { version } = route.request().postDataJSON() as { version: number };
    return route.fulfill({ json: { version: version + 1 } });
  });
  return requests;
}

const atWrite = { hooksConfig: { route: `/books/${book.id}/write` } };

const pane = (page: Page) => page.getByTestId(writeTestIds.preview);
const canvas = (page: Page) => page.getByTestId(writeTestIds.previewCanvas);
const scrollTop = (page: Page) => canvas(page).evaluate((element) => element.scrollTop);

test('Preview_WhenShownFromFormat_ShouldPageTheChapterBeingReadAndRememberIt', async ({
  mount,
  page,
}) => {
  const requests = await stubWrite(page);
  const write = await mount(<AppRoutes />, atWrite);
  await expect(page.getByTestId(writeTestIds.manuscript)).toBeVisible();
  await expect(pane(page)).toHaveCount(0);

  await page.getByTestId(writeTestIds.formatButton).click();
  await page.getByTestId(writeTestIds.formatOption('pane', 'show')).click();

  await expect(page.getByTestId(writeTestIds.previewPages)).toHaveText(/^≈ [3-9] pages$/);
  await expect(page.getByTestId(writeTestIds.previewRendering)).toHaveCount(0);
  await expect(canvas(page)).toContainText('Guyon crossed the yard.');
  await expect(page.getByTestId(writeTestIds.previewScope)).toHaveText(
    `Chapter ${chapter.number}: ${chapter.title}`,
  );
  await expect(page.getByTestId(writeTestIds.previewTemplate)).toHaveValue('novel-6x9');
  await expect(page.getByTestId(writeTestIds.previewHouse)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.previewInLayout)).toHaveAttribute(
    'href',
    `/books/${book.id}/layout#chapter=${chapter.number}`,
  );
  expect(requests[0]).toEqual({ scope: 'chapter', scopeId: chapter.id });
  expect(await page.evaluate((key) => localStorage.getItem(key), previewStorageKey)).toBe('open');

  // Open stays open on the next visit.
  await write.unmount();
  await mount(<AppRoutes />, atWrite);
  await expect(pane(page)).toBeVisible();

  await page.getByTestId(writeTestIds.previewClose).click();
  await expect(pane(page)).toHaveCount(0);
  expect(await page.evaluate((key) => localStorage.getItem(key), previewStorageKey)).toBe('closed');
});

test('Preview_WhenTheShortcutIsPressed_ShouldToggleThePane', async ({ mount, page }) => {
  await stubWrite(page);
  await mount(<AppRoutes />, atWrite);
  await page.getByTestId(writeTestIds.manuscript).focus();

  await page.keyboard.press('Control+Alt+p');
  await expect(pane(page)).toBeVisible();
  await page.keyboard.press('Control+Alt+p');
  await expect(pane(page)).toHaveCount(0);
});

test('Preview_WhenTheProseMovesToAnotherScene_ShouldScrollToItsFirstPage', async ({
  mount,
  page,
}) => {
  await page.evaluate((key) => localStorage.setItem(key, 'open'), previewStorageKey);
  await stubWrite(page);
  await mount(<AppRoutes />, atWrite);
  await expect(page.getByTestId(writeTestIds.previewPages)).toHaveText(/^≈ [3-9] pages$/);
  expect(await scrollTop(page)).toBe(0);

  await page
    .getByTestId(writeTestIds.scene(scene2.id))
    .evaluate((row) => row.scrollIntoView({ block: 'start' }));
  await expect(page.getByTestId(writeTestIds.crumb)).toContainText('Scene 2 of 2');
  await expect(page.getByTestId(SCENE_2_START)).toBeInViewport();
  await expect.poll(() => scrollTop(page)).toBeGreaterThan(0);

  await page
    .getByTestId(writeTestIds.scene(scene1.id))
    .evaluate((row) => row.scrollIntoView({ block: 'start' }));
  await expect(page.getByTestId(writeTestIds.crumb)).toContainText('Scene 1 of 2');
  await expect.poll(() => scrollTop(page)).toBe(0);
});

test('Preview_WhenAPageIsClicked_ShouldShowThatSceneInTheProse', async ({ mount, page }) => {
  await page.evaluate((key) => localStorage.setItem(key, 'open'), previewStorageKey);
  await stubWrite(page);
  await mount(<AppRoutes />, atWrite);
  await expect(page.getByTestId(writeTestIds.crumb)).toContainText('Scene 1 of 2');

  await page.getByTestId(SCENE_2_START).scrollIntoViewIfNeeded();
  await page.getByTestId(SCENE_2_START).click();

  await expect(page.getByTestId(writeTestIds.crumb)).toContainText('Scene 2 of 2');
  await expect(page.getByTestId(writeTestIds.sceneHeading(scene2.id))).toBeInViewport();
});

test('Preview_WhenASceneIsSaved_ShouldRepageInPlace', async ({ mount, page }) => {
  await page.evaluate((key) => localStorage.setItem(key, 'open'), previewStorageKey);
  const requests = await stubWrite(page, (call) => ({
    json: previewOf(call === 1 ? '' : ' Revised.'),
  }));
  await mount(<AppRoutes />, atWrite);
  await expect(page.getByTestId(writeTestIds.previewPages)).toHaveText(/^≈ [3-9] pages$/);
  await canvas(page).evaluate((element) => element.scrollTo({ top: 400 }));

  const editor = page.getByTestId(writeTestIds.scene(scene1.id)).getByTestId(proseTestIds.editor);
  await editor.click();
  await page.keyboard.type('Revised. ');
  await page.getByTestId(writeTestIds.manuscript).focus();

  await expect(canvas(page)).toContainText('Guyon crossed the yard. Revised.');
  expect(requests).toHaveLength(2);
  expect(await scrollTop(page)).toBe(400);
});

test('Preview_WhenThereIsNoPrintToolchain_ShouldSaySoCalmly', async ({ mount, page }) => {
  await page.evaluate((key) => localStorage.setItem(key, 'open'), previewStorageKey);
  await stubWrite(page, () => ({
    status: 503,
    json: { error: 'Pandoc is not configured.', code: 'not_configured' },
  }));
  await mount(<AppRoutes />, atWrite);

  await expect(page.getByTestId(writeTestIds.previewUnavailable)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.previewError)).toHaveCount(0);
  await expect(page.getByTestId(writeTestIds.previewInLayout)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.manuscript)).toBeVisible();
});
