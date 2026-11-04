import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import type { LayoutPreview, LayoutState } from '@codex/core/api/layout';
import { AppRoutes } from '../../AppRoutes';
import { allBooks, headerFor, stubShellApi } from '../../test-support/shellStubs';
import { layoutTestIds } from './testIds';

// Layout below the write role (#80): the preview works; the template, export and proof do not.
const book = allBooks(library)[0];
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };
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
      hasPreviewCss: true,
      notices: [],
      options: [
        {
          key: 'toc',
          label: 'Table of contents',
          type: 'boolean',
          choices: [],
          default: false,
          value: false,
        },
      ],
    },
  ],
};

const preview: LayoutPreview = {
  html:
    `<h2 id="ch-${chapter.id}">${chapter.title}</h2>` +
    `<div class="codex-scene" data-scene="${scene.id}" data-chapter="${chapter.id}" data-label="Ch 1 · Sc 1"></div>` +
    '<p>Guyon crossed the yard.</p>',
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

async function openLayout(page: Page, role: string) {
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}`, (route) =>
    route.fulfill({ json: { ...headerFor(book), role, owner } }),
  );
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/layout`, (route) => route.fulfill({ json: layout }));
  await page.route(`**/api/books/${book.id}/layout/preview`, (route) =>
    route.fulfill({ json: preview }),
  );
}

test('Layout_WhenViewOnly_ShouldPreviewButNotExportProofOrChangeTheTemplate', async ({
  mount,
  page,
}) => {
  await openLayout(page, 'view');
  await mount(<AppRoutes />, {
    hooksConfig: { route: `/books/${book.id}/layout#chapter=${chapter.number}` },
  });

  await expect(page.getByTestId(layoutTestIds.statusPages)).toHaveText('1 page');
  await expect(page.getByTestId(layoutTestIds.exportOpen)).toHaveCount(0);
  await expect(page.getByTestId(layoutTestIds.templateSelect)).toBeDisabled();
  await expect(page.getByTestId(layoutTestIds.trimSelect)).toBeDisabled();
  await expect(page.getByTestId(layoutTestIds.modeProof)).toBeDisabled();
  await expect(
    page.getByTestId(layoutTestIds.option('toc')).getByRole('button').first(),
  ).toBeDisabled();
});

test('Layout_WhenWrite_ShouldOfferExportAndTheTemplate', async ({ mount, page }) => {
  await openLayout(page, 'write');
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/layout` } });

  await expect(page.getByTestId(layoutTestIds.statusPages)).toHaveText('1 page');
  await expect(page.getByTestId(layoutTestIds.exportOpen)).toBeVisible();
  await expect(page.getByTestId(layoutTestIds.templateSelect)).toBeEnabled();
});
