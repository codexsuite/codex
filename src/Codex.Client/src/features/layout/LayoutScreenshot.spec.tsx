import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import type { LayoutPreview, LayoutState } from '@codex/core/api/layout';
import { AppRoutes } from '../../AppRoutes';
import { allBooks, stubShellApi } from '../../test-support/shellStubs';
import { layoutTestIds } from './testIds';

test.use({ viewport: { width: 1440, height: 900 } });

const book = allBooks(library)[0];
const chapter = outline.acts.flatMap((a) => a.chapters).find((c) => c.scenes.length > 0)!;
const [scene] = chapter.scenes;

const layout: LayoutState = {
  template: 'novel-6x9',
  trim: '6x9',
  templates: [
    {
      slug: 'novel-6x9',
      name: 'Novel (6 x 9)',
      version: '1.0.0',
      engine: 'tectonic',
      trims: ['6x9', '5.5x8.5'],
      columns: 1,
      hasPreviewCss: false,
      notices: [],
      options: [],
    },
  ],
};

const preview: LayoutPreview = {
  html:
    `<h2 id="ch-${chapter.id}">${chapter.title}</h2>` +
    `<div class="codex-scene" data-scene="${scene.id}" data-chapter="${chapter.id}" data-label="Ch 1 · Sc 1"></div>` +
    '<p>Guyon crossed the yard.</p><p>The guide was waiting by the gate.</p>',
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
};

test('LayoutPage_ShouldMatchScreenshot', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/layout`, (route) => route.fulfill({ json: layout }));
  await page.route(`**/api/books/${book.id}/layout/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  await expect(page.getByTestId(layoutTestIds.statusPages)).toHaveText('1 page');
  await expect(page.getByTestId(layoutTestIds.statusRendering)).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot('layout-default-light.png');

  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page).toHaveScreenshot('layout-default-dark.png');
});
