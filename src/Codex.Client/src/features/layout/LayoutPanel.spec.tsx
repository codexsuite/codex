import { expect, test } from '@playwright/experimental-ct-react';
import type { LayoutPreview, LayoutState } from '@codex/core/api/layout';
import { LayoutPanel } from './LayoutPanel';
import { layoutTestIds } from './testIds';
import type { ExportSession } from './useExports';

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

const preview = {
  flags: [{ kind: 'raw_latex', sceneId: 's1', detail: '\\begin{tabular}', kept: false }],
  figures: [
    {
      figureId: 'f1',
      assetId: 'a1',
      sceneId: 's1',
      filename: 'map.png',
      caption: null,
      placement: 'column',
      widthPx: 480,
      heightPx: 320,
      placedWidthIn: 4.5,
      placedHeightIn: 3,
      effectiveDpi: 106.7,
      status: 'low',
    },
  ],
} as unknown as LayoutPreview;

const exportSession: ExportSession = {
  list: undefined,
  error: null,
  cancel: () => undefined,
  queue: () => Promise.resolve(null),
};

const sceneInfo = (id: string) =>
  id === 's1' ? { id, number: '1.1', label: 'Ch 1 · Sc 1' } : undefined;

test('LayoutPanel_WhenAnOptionIsPressed_ShouldReportItsValue', async ({ mount, page }) => {
  const changes: Array<[string, unknown]> = [];
  await mount(
    <LayoutPanel
      bookId="b"
      layout={layout}
      preview={preview}
      sceneInfo={sceneInfo}
      onOption={(key, value) => changes.push([key, value])}
      onShow={() => undefined}
      fingerprint=""
      exports={exportSession}
      onExportAgain={() => undefined}
    />,
  );

  await page.getByTestId(layoutTestIds.option('toc')).getByRole('button', { name: 'On' }).click();

  expect(changes).toEqual([['toc', true]]);
});

test('LayoutPanel_WhenTheIssuesTabIsOpen_ShouldListLowDpiAndRawLatexWithShowOnPage', async ({
  mount,
  page,
}) => {
  const shown: string[] = [];
  await mount(
    <LayoutPanel
      bookId="b"
      layout={layout}
      preview={preview}
      sceneInfo={sceneInfo}
      onOption={() => undefined}
      onShow={(id) => shown.push(id)}
      fingerprint=""
      exports={exportSession}
      onExportAgain={() => undefined}
    />,
  );

  await page.getByTestId(layoutTestIds.tab('issues')).click();
  await expect(page.getByTestId(layoutTestIds.issueItem)).toHaveCount(2);
  await expect(page.getByTestId(layoutTestIds.issueItem).first()).toContainText('106.7 dpi');
  await page.getByTestId(layoutTestIds.showOnPage).first().click();

  expect(shown).toEqual(['s1']);
});
